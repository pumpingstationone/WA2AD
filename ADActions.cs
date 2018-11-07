using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;

namespace WA2AD
{
    class ADActions
    {  
        private PrincipalContext pc = null;
  
        private void addOthePager(Principal userPrincipal, string rfidTag)
        {
            if (rfidTag == null || rfidTag.Length == 0)
                return;

            userPrincipal.Save();
            DirectoryEntry de = (userPrincipal.GetUnderlyingObject() as DirectoryEntry);
            if (de != null)
            {
#if DEBUG
                DirectorySearcher deSearch = new DirectorySearcher(de);
                deSearch.PropertiesToLoad.Add("otherPager");
                SearchResultCollection results = deSearch.FindAll();
                if (results != null && results.Count > 0)
                {
                    ResultPropertyCollection rpc = results[0].Properties;
                    foreach (string rp in rpc.PropertyNames)
                    {
                        if (rp == "otherpager")
                        {
                            int pagerCount = rpc["otherpager"].Count;
                            for (int x = 0; x < pagerCount; ++x)
                            {
                                Console.WriteLine(rpc["otherpager"][x].ToString());
                            }
                        }
                    }
                }
#endif
                de.Properties["otherPager"].Add(rfidTag);
                de.CommitChanges();
            }
        }

        private FieldValue getValueForKey(Member member, string key)
        {
            for (int x = 0; x < member.FieldValues.Count; ++x)
            {
                if (member.FieldValues[x].FieldName == key)
                {
                    return member.FieldValues[x];
                }
            }

            return null;
        }

        private void CreateUser(Member member)
        {
            // Don't create the user if not active
            if (member.MembershipEnabled == false)
                return;

            UserPrincipal userPrincipal = new UserPrincipal(this.pc);

            if (member.LastName != null && member.LastName.Length > 0)
                userPrincipal.Surname = member.LastName;

            if (member.FirstName != null && member.FirstName.Length > 0)
                userPrincipal.GivenName = member.FirstName;

            if (member.Email != null && member.Email.Length > 0)
                userPrincipal.EmailAddress = member.Email;
            else
            { 
                Console.WriteLine("No email set for " + member.FirstName + " " + member.LastName + ", so can't continue.");
                return;
            }

            string userLogonName = (string)member.FieldValues[FieldValue.ADUSERNAME].Value;
            if (userLogonName != null && userLogonName.Length > 0)
                // Apparently we can only use the first twenty characters for this name
                userPrincipal.SamAccountName = userLogonName.Substring(0, 20);
            else
            {
                Console.WriteLine("No username set for " + member.FirstName + " " + member.LastName + ", so can't continue.");
                return;
            }

            // Hypothetically they should always have an email, but let's be
            // careful anyway
            if (member.Email != null && member.Email.Length > 0)
                // Can only use the first 256 characters (though never seen an
                // email address that long, but okay....)
                userPrincipal.UserPrincipalName = member.Email.Substring(0, 256);

            // The user may have an RFID tag       
            FieldValue rfidTagFV = getValueForKey(member, "RFID Tag");
            if (rfidTagFV != null && rfidTagFV.ToString().Length > 0)
            {
                string rfidTag = (string)rfidTagFV.Value;
                addOthePager(userPrincipal, rfidTag); 
            }

            String pwdOfNewlyCreatedUser = "ps1@@12345!~";
            userPrincipal.SetPassword(pwdOfNewlyCreatedUser);
            userPrincipal.PasswordNotRequired = false;

            userPrincipal.Enabled = true;
            userPrincipal.ExpirePasswordNow();
           
            try
            {
                userPrincipal.Save();
                Console.WriteLine("Created a new user for " + member.FirstName + " " + member.LastName);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception creating user object for " + member.FirstName + " " + member.LastName + " -> " + e);
            }
        }

        private void UpdateUser(Member member, ref UserPrincipal userPrincipal)
        {
            // Just disable the password right now if we need to
            bool isCurrentlyEnabled = (bool)userPrincipal.Enabled;
            bool shouldBeEnabled = member.MembershipEnabled;
            
            if (isCurrentlyEnabled != shouldBeEnabled)
            {
                Console.WriteLine("Going to set " + member.FirstName + " " + member.LastName + "'s status to " + (shouldBeEnabled ? "enabled" : "disabled"));
                userPrincipal.Enabled = shouldBeEnabled;
            }

            // The user may have updated their RFID tag   
            FieldValue rfidTagFV = getValueForKey(member, "RFID Tag");
            if (rfidTagFV != null && rfidTagFV.ToString().Length > 0)
            {
                string rfidTag = (string)rfidTagFV.Value;
                addOthePager(userPrincipal, rfidTag);
            }      

            try
            {
                userPrincipal.Save();
                Console.WriteLine("Updated user " + member.FirstName + " " + member.LastName);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception creating user object. " + e);
            }
        }

        private bool FindExistingUser(ref UserPrincipal user)
        {
           
            PrincipalSearcher search = new PrincipalSearcher(user);
            UserPrincipal result = (UserPrincipal)search.FindOne();
            search.Dispose();

            if (result is null)
                return false;

            user = result;
            return true;
        }

        public ADActions()
        {
            // The username with Domain Admin or comparable rights
            // in <Domain>\<User> format
            string username = @""; 
            string password = @"";
            // The AD server name or IP address
            string adServer = @"";
            // The LDAP path to the users
            // (e.g. CN=users,DC=ad,DC=organizationname,DC=org)
            string usersPath = @"";

            try
            {
                this.pc = new PrincipalContext(ContextType.Domain, @adServer, @usersPath, ContextOptions.Negotiate, username, password);               
            }
            catch (Exception e)
            {
                Console.WriteLine("Hmm, failed to create PrincipalContext. Exception is: " + e);
            }
        }

        public void HandleMember(Member member)
        {
            UserPrincipal u = new UserPrincipal(pc)
            {
                SamAccountName = (string)member.FieldValues[FieldValue.ADUSERNAME].Value
            };

            if (FindExistingUser(ref u)) 
            {
                Console.WriteLine("Oh, hey, found " + member.FirstName + " in AD");
                UpdateUser(member, ref u);
            }
            else
            {
                Console.WriteLine("Didn't find " + member.FirstName + " in AD, so must be new...");
                CreateUser(member);
            }

            u.Save();
        }
    }
}
