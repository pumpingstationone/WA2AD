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
            try
            {
                foreach (var obj in waData.GetMemberData().GetValue("Contacts"))
                {
                    Member member = (Member)obj.ToObject<Member>();

                    Console.WriteLine("Going to work with " + member.FirstName + " " + member.LastName);
                    try
                    { 
                        adActions.HandleMember(member);
                    }
                    catch (Exception me)
                    {
                        Console.WriteLine("Hmm, An error occurred when working with {0}: '{1}'", member.FirstName, me);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("An error occurred: '{0}'", e);
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting...");

            new Program();
        }
    }
}
