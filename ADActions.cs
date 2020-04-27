﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace WA2AD
{
    class ADActions
    {
        // For writing to the system event log (See program.cs for how to make sure this works)
        private EventLog appLog;

        private PrincipalContext pc = null;

        // The OU, read from the INI file, that we want to save the users to
        private string membersPath;

        // This is not cryptographically secure, and we are, in fact, using it for
        // passwords, but we are generating useless, unknowable passwords that are
        // not recorded anywhere; the user must go to the self-service portal to change
        // his or her password to the "real" one they want to use, so nobody is really
        // dependent on this level of security of thrown-away passwords.
        private static Random random = new Random();
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        // This method will add the users to the appropriate "computer groups" they
        // have been authorized on.
        // WARNING! The assumption is that the name of the authorization in Wild Apricot
        // is *exactly* the same name as the OU in Active Directory. In other words, if
        // the user is authorized in "Boss Authorized Users" in WA, then the name of the
        // OU in Active Directory *must* be "Boss Authorized Users".
        private void addUserToGroups(string sectionName, ref UserPrincipal userPrincipal, Member member)
        {
            FieldValue authGroups = getValueForKey(member, sectionName);
            if (authGroups != null)
            {
                JArray authsObj = (JArray)authGroups.Value;
                if (authsObj != null && authsObj.Count() > 0)
                {
                    for (int x = 0; x < authsObj.Count(); ++x)
                    {
                        JToken auth = authsObj[x];

                        string groupName = auth.Value<string>("Label");
                        Console.WriteLine("Going to add to the " + groupName + " group");                        
                        GroupPrincipal group = GroupPrincipal.FindByIdentity(this.pc, groupName);

                        try
                        {
                            group.Members.Add(this.pc, IdentityType.SamAccountName, userPrincipal.SamAccountName);
                            group.Save();
                        }
                        catch (System.DirectoryServices.AccountManagement.PrincipalExistsException pe)
                        {
                            Console.WriteLine("...but the user is already in the group");
                        }
                    }
                }
            }
        }

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

            // We create a specific context when creating a new member so the object is stored
            // in the right place in the domain (as set in the ini file)
            PrincipalContext memberCtx = new PrincipalContext(ContextType.Domain, null, this.membersPath);

            UserPrincipal userPrincipal = new UserPrincipal(memberCtx);
            
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
                userPrincipal.SamAccountName = userLogonName.Length > 20 ? userLogonName.Substring(0, 20) : userLogonName;
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
                userPrincipal.UserPrincipalName = member.Email.Length > 256 ? member.Email.Substring(0, 256) : member.Email;

            // The user may have an RFID tag       
            FieldValue rfidTagFV = getValueForKey(member, "RFID Tag");
            if (rfidTagFV != null && rfidTagFV.ToString().Length > 0)
            {
                string rfidTag = (string)rfidTagFV.Value;
                addOthePager(userPrincipal, rfidTag); 
            }

            // Generate a useless password that the user doesn't know so
            // he or she must create a new one.
            String pwdOfNewlyCreatedUser = RandomString(15);
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

            // They may have updated their email address
            if (userPrincipal.UserPrincipalName != member.Email && (member.Email != null && member.Email.Length > 0))
                // Can only use the first 256 characters (though never seen an
                // email address that long, but okay....)
                userPrincipal.UserPrincipalName = member.Email.Length > 256 ? member.Email.Substring(0, 256) : member.Email;


            // The user may have updated their RFID tag   
            FieldValue rfidTagFV = getValueForKey(member, "RFID Tag");
            if (rfidTagFV != null && rfidTagFV.ToString().Length > 0)
            {
                // Split on a comma
                string[] tokens = ((string)rfidTagFV.Value).Split(',');
                foreach (var rfidTag in tokens)
                {                 
                    // Add the tag, but make sure there aren't any spaces around it
                    addOthePager(userPrincipal, rfidTag.Trim());
                }
            }

            // And update their group memberships
            addUserToGroups("Computer Authorizations", ref userPrincipal, member);
            // And do the same thing for the other authorizations (e.g. welders, lathe, etc.)
            addUserToGroups("Authorizations", ref userPrincipal, member);

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
            this.appLog = new EventLog("Application");
            appLog.Source = "WA2AD";

            //
            // Load the active directory settings from the ini file. If there
            // aren't any then we'll move blindly ahead assuming that the machine
            // running the program is on the domain we want to use, and that the
            // user running the program (typically a service account) has the
            // proper rights to manipulate AD objects
            //

            var MyIni = new IniFile();

            // The username with Domain Admin or comparable rights
            // in <Domain>\<User> format
            string username = MyIni.Read("ADUser").Trim(); 
            string password = MyIni.Read("ADPassword").Trim();
            // The AD server name or IP address
            string adServer = MyIni.Read("ADIPAddress").Trim();
            // The LDAP path to the users
            // (e.g. CN=users,DC=ad,DC=organizationname,DC=org)
            this.membersPath = MyIni.Read("ADUsersOU").Trim();

            // If we don't have a CN, that's bad because we really need that one
            if (this.membersPath.Length == 0)
            {
                appLog.WriteEntry("WHOA! The CN needs to be set in the ini file! (The ADUsersOU property). Not going to continue because I don't know where to put anything!", EventLogEntryType.Error);
                Console.WriteLine("WHOA! The CN needs to be set in the ini file! (The ADUsersOU property). Not going to continue because I don't know where to put anything!");
                return;
            }
            else
            {
                appLog.WriteEntry(string.Format("Going to work with member objects in {0}", this.membersPath));
                Console.WriteLine("Working member objects with: " + this.membersPath);
            }

            // If we have a user/password/IP combo, then we'll assume
            // we're currently running on a machine that is *not* on the
            // domain we want to work with.
            if (username.Length == 0 || password.Length == 0 || adServer.Length == 0)
            {
                Console.WriteLine("Ok, we're going to connect assuming we're on the domain, run by a user with appropriate permissions");
                // We need to use this context so we have full access to the domain, and not just one part of it
                this.pc = new PrincipalContext(ContextType.Domain);
            }
            else
            {
                // We have all the credentials, so we're going to try to connect using those
                Console.WriteLine("Going to connect with credentials...");
                try
                {                    
                    this.pc = new PrincipalContext(ContextType.Domain, null, username, password);               
                }
                catch (Exception e)
                {
                    appLog.WriteEntry(string.Format("Hmm, failed to create PrincipalContext. Exception is: {0}", e), EventLogEntryType.Error);
                    Console.WriteLine("Hmm, failed to create PrincipalContext. Exception is: " + e);
                }
            }
        }

        public void HandleMember(Member member)
        {
            // Is this a real member, or just a contact?
            if (member.MembershipLevel == null)
            {
                Console.WriteLine("This person is not a member!");
                return;
            }

            // But do nothing if the membership is still pending
            if (member.Status == "PendingNew")
            {
                Console.WriteLine("Ah, but membership is still pending, so not going to add");
                return;
            }

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
        }
    }
}
