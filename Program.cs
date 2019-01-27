using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

//
// For this program to write to the Event Log properly, run
// this command using an elevated power shell:
//
//       New-EventLog -LogName Application -Source "WA2AD"
//

namespace WA2AD
{
    class Program
    {
        private WAData waData = new WAData();
        private ADActions adActions = new ADActions();

        public Program()
        {
            var appLog = new EventLog("Application");
            appLog.Source = "WA2AD";
            appLog.WriteEntry("Beginning job...");

            try
            {
                foreach (var obj in waData.GetMemberData().GetValue("Contacts"))
                {
                    Member member = (Member)obj.ToObject<Member>();
                                      
                    try
                    {
                        Console.WriteLine("Going to work with " + member.FirstName + " " + member.LastName);
                        adActions.HandleMember(member);
                    }
                    catch (Exception me)
                    {                        
                        appLog.WriteEntry(string.Format("Hmm, An error occurred when working with {0}: '{1}'", member.FirstName, me), EventLogEntryType.Error);
                        Console.WriteLine("Hmm, An error occurred when working with {0}: '{1}'", member.FirstName, me);
                    }
                }
            }
            catch (Exception e)
            {
                appLog.WriteEntry(string.Format("An error occurred: '{0}'", e), EventLogEntryType.Error);
                Console.WriteLine("An error occurred: '{0}'", e);
            }

            appLog.WriteEntry("...Finished job");
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting...");

            new Program();
        }
    }
}
