using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Mail;
using System.Net;
using System.Text;

namespace CheckService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    //считываем все таски
                    Common.ConfHasChanges();
                    if (Common.ActualTasks.Length != 0)
                    {
                        
                        var alreadyWorked = new List<KeyValuePair<string, DateTime>>();

                        if (!Directory.Exists(Common.syslog))
                            Directory.CreateDirectory(Common.syslog);
                        else
                        {
                            var fname = Common.syslog + "\\Log_" + DateTime.Now.ToString("yyyy_MM_dd") + ".log";
                            //смотрим когда и что выполнялось по системному логу
                            if (File.Exists(fname))
                            {
                                var syslog = File.ReadAllLines(fname).Where(m => !string.IsNullOrEmpty(m)).ToArray();
                                for (int i = 0; i < syslog.Length - 1; i++)
                                {
                                    if (Regex.IsMatch(syslog[i], @"[a-zA-Zа-яА-Я0-9]+") && Regex.IsMatch(syslog[i + 1], @"\d{1,2}\.\d{1,2}\.\d{4}\ \d{1,2}\:\d{1,2}\:\d{1,2}"))
                                        alreadyWorked.Add(new KeyValuePair<string, DateTime>(syslog[i], DateTime.Parse(syslog[i + 1])));
                                }
                            }
                        }

                        List<STask> plannedTasks = new List<STask>();
                        //отбрасываем, что уже сегодня выполнялось (в соответствии с индивидуальным расписанием)
                        foreach (var task in Common.ActualTasks.Where(m=>!string.IsNullOrEmpty(m.Name)))
                        {
                            if (task.ScheduleDT.Count == 1)
                            {
                                DateTime pdate = DateTime.Now.Date.AddHours(task.ScheduleDT.FirstOrDefault().TotalHours);
                                if (DateTime.Now > pdate)
                                {
                                    var c = alreadyWorked.Where(m => m.Key == task.Name && m.Value.Date == pdate.Date);                                  
                                    if (c.Count() == 0)
                                        plannedTasks.Add(task);
                                }
                            }
                            else
                            {
                                foreach (var pvar in task.ScheduleDT.OrderBy(m => m)) 
                                {
                                    DateTime pdate = DateTime.Now.Date.AddHours(pvar.TotalHours);
                                  
                                    if (DateTime.Now.Date == pdate.Date && (int)DateTime.Now.TimeOfDay.TotalHours == (int)pdate.TimeOfDay.TotalHours)
                                    {
                                        var c = alreadyWorked.Where(m => m.Key == task.Name && m.Value.Date == pdate.Date && (int)m.Value.TimeOfDay.TotalHours == (int)pdate.TimeOfDay.TotalHours);                                       
                                        if (c.Count() == 0)
                                        {
                                            plannedTasks.Add(task);
                                            break;
                                        }
                                    }
                                }
                            }
                        }


                        if (plannedTasks.Count != 0)
                        {                            
                            foreach (STask task in plannedTasks)
                            {
                                using (DataTable dt = new DataTable())
                                {
                                    try
                                    {
                                        using (var sql = new SqlConnection())
                                        {
                                            sql.ConnectionString = task.ConnectionString;
                                            sql.Open();
                                            using (var cmd = new SqlCommand())
                                            {
                                                cmd.Connection = sql;
                                                cmd.CommandType = string.IsNullOrEmpty(task.SqlStoredProcedure) ? System.Data.CommandType.Text : System.Data.CommandType.StoredProcedure;
                                                cmd.CommandText = string.IsNullOrEmpty(task.SqlStoredProcedure) ? task.SqlQuery : task.SqlStoredProcedure;

                                                dt.Load(cmd.ExecuteReader());

                                                if (dt.Rows.Count != 0)
                                                {
                                                    //собираем ошибки
                                                    string errstr = "";
                                                    foreach (DataRow row in dt.Rows)
                                                    {
                                                        foreach (DataColumn col in dt.Columns)
                                                        {
                                                            if(row[col.ColumnName] != DBNull.Value)
                                                            errstr += "<span>" + row[col.ColumnName].ToString() + "</span> ";
                                                        }
                                                        errstr += "<br>";
                                                    }

                                                    throw new Exception(errstr);
                                                }

                                             
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        task.IsError = true;
                                        task.ErrorText = ex.Message;
                                        Log.Write(ex);
                                    }
                                }
                                //пишем лог для следующего цикла
                                Log.Write(task);
                            }

                            var errors = plannedTasks.Where(m => m.IsError);
                            if (errors.Count() != 0)
                            {
                                try
                                {
                                    using (MailMessage m = new MailMessage())
                                    {
                                        m.From = new MailAddress(Common.sender.Login);                                       
                                        foreach (var r in Common.recipients)
                                        {
                                            if (Regex.IsMatch(r, @".+\@.+\.\w{2,10}"))
                                                m.To.Add(new MailAddress(r));
                                        }
                                        m.Subject = Common.sender.Subject;
                                        m.IsBodyHtml = true;
                                        m.Body = "<h3>" + DateTime.Now + "</h3>";
                                        foreach (var error in errors)
                                        {
                                            m.Body += "<p><span>" + error.Name + " : </span>" + error.ErrorText + "</p>";
                                        }
                                        m.BodyEncoding = Encoding.UTF8;

                                        using (SmtpClient smtp = new SmtpClient(Common.sender.Server, Common.sender._port))
                                        {                                            
                                            smtp.Credentials = new NetworkCredential(Common.sender.Login, Common.sender.Pass);
                                            smtp.EnableSsl = true;
                                            smtp.Send(m);
                                        }
                                    }
                                }
                                catch (Exception x)
                                {
                                    Log.Write(x);
                                }
                            }
                        }

                    }
                }
                catch (Exception e)
                {
                    Log.Write(e);
                }
                GC.Collect();
                GC.GetTotalMemory(true);                
                await Task.Delay(1000*60*15, stoppingToken);
     
            }
        }
    }
}
