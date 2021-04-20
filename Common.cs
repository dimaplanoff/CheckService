using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace CheckService
{
    public class Sender
    {
        public string User { get; set; }
        public string Server { get; set; }
        public string Login { get; set; }
        public string Pass { get; set; }
        public string Subject { get; set; }
        public int _port { get; set; }
        public string Port { set { _port = int.Parse(value); } }
        public string Boby { get; set; }
  
    }

    public class STask
    {
        public string Name { get; set; } = string.Empty;
        public string ErrorText { get; set; } = string.Empty;
        public bool IsError { get; set; } = false;
        public string ConnectionString { get; set; } = string.Empty;
        public List<TimeSpan> ScheduleDT = new List<TimeSpan>();
        private string _sql_query = string.Empty;
        public string SqlQuery
        { 
            get 
            {
                return _sql_query; 
            } 
            set 
            {
                try //запрещаем прямые инструкции на изменение
                {
                    var tval = value.ToLower();

                    if (tval.Contains("update "))
                        throw new Exception();
                    if (tval.Contains("insert "))
                        throw new Exception();
                    if (tval.Contains("delete "))
                        throw new Exception();
                    if (tval.Contains("create "))
                        throw new Exception();

                    _sql_query = value;
                }
                catch
                {
                    _sql_query = string.Empty;
                }
            } 
        }
        public string SqlStoredProcedure { get; set; } = string.Empty;
        public string Schedule { 
            set 
            {
                ScheduleDT = new List<TimeSpan>();
                string val = value.ToLower();
                if (val.StartsWith("day"))
                    ScheduleDT.Add(Common.DefaultSchedule);
                if (Regex.IsMatch(val, @"\d{1,2}([0-9;])*"))
                {
                    foreach (var t in val.Split(';'))
                    {
                        try
                        {
                            var dt = TimeSpan.FromHours(int.Parse(t));
                            if (!ScheduleDT.Contains(dt))
                                ScheduleDT.Add(dt);
                        }
                        catch { }
                    }
                }
            } 
        }        
    }

    class Common
    {
        public static STask[] ActualTasks = new STask[0];
        public static Sender sender = null;
        public static string[] recipients = null;
        public static DateTime ConfigLastChange = new DateTime();
        public static TimeSpan DefaultSchedule = DateTime.Now.TimeOfDay;        
        public static string path = AppDomain.CurrentDomain.BaseDirectory + "appsettings.json";
        public static string syslog = AppDomain.CurrentDomain.BaseDirectory + "SysLog";

        public static void ConfHasChanges()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
       

            FileInfo fi = new FileInfo(path);
            //if (fi.LastWriteTime > ConfigLastChange)
            {
                ActualTasks = new STask[0];
                using (var file = new StreamReader(path, Encoding.GetEncoding(1251)))
                {
                    var jr = new JsonTextReader(file);
                    var j = JObject.Load(jr);
                    var tmp_dt = j.GetValue("DefaultSchedule").Value<string>();
                    if (tmp_dt != null && !string.IsNullOrEmpty(tmp_dt) && int.TryParse(tmp_dt, out int defTime))
                        DefaultSchedule = TimeSpan.FromHours(defTime);

                    var tmp_sender = j.GetValue("MailConf").ToString();
                    sender = JsonConvert.DeserializeObject<Sender>(tmp_sender);

                    recipients = JsonConvert.DeserializeObject<string[]>(j.GetValue("MailRecipient").ToString()); 

                    var tmp_data = j.GetValue("Data").ToString();
                    ActualTasks = JsonConvert.DeserializeObject<STask[]>(tmp_data);                    
                }
                ConfigLastChange = fi.LastWriteTime;                
            }            
        }

    }

    public class Log
    {
        private static object s = new object();
        public static void Write(Exception e)
        {
            Task.Run(() => {
                lock (s)
                {
                    try
                    {
                        if (!Directory.Exists(AppDomain.CurrentDomain.BaseDirectory + "ErrorLog"))
                            Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory + "ErrorLog");

                        using (var sw = new StreamWriter(AppDomain.CurrentDomain.BaseDirectory + "ErrorLog\\Log_" + DateTime.Now.ToString("yyyy_MM_dd_HH") + ".log", true))
                        {
                            sw.WriteLine("\r\n" + DateTime.Now + "   :   " + e.Message);
                            if (e.StackTrace != null && !string.IsNullOrEmpty(e.StackTrace))
                                sw.WriteLine("  " + e.StackTrace);
                        }
                    }
                    catch { }
                }
            });
        }
        public static void Write(STask t)
        {
            Task.Run(() => {
                lock (s)
                {

                    if (!Directory.Exists(Common.syslog))
                        Directory.CreateDirectory(Common.syslog);
                    else
                    {
                        foreach (var f in Directory.GetFiles(Common.syslog))
                        {
                            var fi = new FileInfo(f);
                            if (fi.CreationTime < DateTime.Now.AddDays(-7))
                                File.Delete(f);
                        }
                    }

                    try
                    {
                        using (var sw = new StreamWriter(Common.syslog + "\\Log_" + DateTime.Now.ToString("yyyy_MM_dd") + ".log", true))
                        {
                            sw.WriteLine("\r\n" + t.Name);
                            sw.WriteLine(DateTime.Now);
                        }
                    }
                    catch { }
                }
            });
        }
    }
}
