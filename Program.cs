using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace WA2AD
{
    class Program
    {
        private WAData waData = new WAData();
        private ADActions adActions = new ADActions();

        public Program()
        {
            foreach (var obj in waData.GetMemberData().GetValue("Contacts"))
            {
                Member member = (Member)obj.ToObject<Member>();
               
                Console.WriteLine("Going to work with " + member.FirstName + " " + member.LastName);
                adActions.HandleMember(member);
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting...");

            new Program();
        }
    }
}
