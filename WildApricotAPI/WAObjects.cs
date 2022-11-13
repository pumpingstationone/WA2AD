﻿using System;
using System.Collections.Generic;

namespace WildApricotAPI
{
    public class MembershipLevel
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string Name { get; set; }
    }

    public class FieldValue
    {
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
        public string Password { get; set; }
    }
}
