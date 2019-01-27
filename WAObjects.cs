using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace WA2AD
{
    public class MembershipLevel
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string Name { get; set; }
    }

    public class FieldValue
    {
        public const int ADUSERNAME = 44;
        public const int RFIDTAG = 48;
        public const int COMPUTERAUTHS = 51;

        public string FieldName { get; set; }
        public object Value { get; set; }
        public string SystemCode { get; set; }
    }

    public class Member
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Organization { get; set; }
        public DateTime ProfileLastUpdated { get; set; }
        public MembershipLevel MembershipLevel { get; set; }
        public bool MembershipEnabled { get; set; }
        public string Status { get; set; }
        public List<FieldValue> FieldValues { get; set; }
        public int Id { get; set; }
        public string Url { get; set; }
        public bool IsAccountAdministrator { get; set; }
        public bool TermsOfUseAccepted { get; set; }
    }
}
