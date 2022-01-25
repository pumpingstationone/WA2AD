using System;
using System.Linq;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using WildApricotAPI;

namespace WA2AD
{
    class ADActions
    {      
        private PrincipalContext pc = null;

        // Security credentials in case we need them
        private string username;
        private string password;
        private string adServer;

        // The OU, read from the INI file, that we want to save the members to
        private string membersPath;

        // The OU of the inactive member path
        private string inactiveMembersPath;

        // The field in AD that we use to store RFID tags. This is an array of strings
        private string RFID_FIELD = "otherPager";

        // This is not cryptographically secure, and we are, in fact, using it for
        // passwords, but we are generating useless, unknowable passwords that are
        // not recorded anywhere; the user must go to the self-service portal to change
        // his or her password to the "real" one they want to use, so nobody is really
        // dependent on this level of security of thrown-away passwords.
        private static Random random = new Random();

        // Our connection to the Azure B2C stuff
        private B2CActions b2cActions = new B2CActions();

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


        // This method clears out the RFID tag array for the user in preparation
        // for the tags to be added from Wild Apricot. By resetting the array, we
        // handle any removes or edits, and thus can be sure that whatever is in
        // the array when we're all done is what is really reflective of what the
        // user's tag status is in WA.
        private void resetRFIDField(Principal userPrincipal)
        {
            userPrincipal.Save();
            DirectoryEntry de = (userPrincipal.GetUnderlyingObject() as DirectoryEntry);
            if (de != null)
            {
                de.Properties[RFID_FIELD].Clear();
                de.CommitChanges();
            }
        }

        private void addRFIDTag(Principal userPrincipal, string rfidTag)
        {
            if (rfidTag == null || rfidTag.Length == 0)
                return;

            userPrincipal.Save();
            DirectoryEntry de = (userPrincipal.GetUnderlyingObject() as DirectoryEntry);
            if (de != null)
            {
#if DEBUG
                DirectorySearcher deSearch = new DirectorySearcher(de);
                deSearch.PropertiesToLoad.Add(RFID_FIELD);
                SearchResultCollection results = deSearch.FindAll();
                if (results != null && results.Count > 0)
                {
                    ResultPropertyCollection rpc = results[0].Properties;
                    foreach (string rp in rpc.PropertyNames)
                    {
                        if (rp == RFID_FIELD)
                        {
                            int pagerCount = rpc[RFID_FIELD].Count;
                            for (int x = 0; x < pagerCount; ++x)
                            {
                                Console.WriteLine(rpc[RFID_FIELD][x].ToString());
                            }
                        }
                    }
                }
#endif
                de.Properties[RFID_FIELD].Add(rfidTag);
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
            PrincipalContext memberCtx;
            if (this.username.Length > 0)
            {
                memberCtx = new PrincipalContext(ContextType.Domain, this.adServer, this.membersPath, this.username, this.password);
            }
            else
            {
                memberCtx = new PrincipalContext(ContextType.Domain, null, this.membersPath);
            }

            UserPrincipal userPrincipal = new UserPrincipal(memberCtx);
            
            if (member.LastName != null && member.LastName.Length > 0)
                userPrincipal.Surname = member.LastName;

            if (member.FirstName != null && member.FirstName.Length > 0)
                userPrincipal.GivenName = member.FirstName;

            if (member.Email != null && member.Email.Length > 0)
                userPrincipal.EmailAddress = member.Email;
            else
            { 
                Log.Write(Log.Level.Warning, "(mid:"+member.Id+") No email set for " + member.FirstName + " " + member.LastName + ", so can't continue.");
                return;
            }

            string userLogonName = (string)member.FieldValues[FieldValue.ADUSERNAME].Value;
            if (userLogonName != null && userLogonName.Length > 0)
                // Apparently we can only use the first twenty characters for this name
                userPrincipal.SamAccountName = userLogonName.Length > 20 ? userLogonName.Substring(0, 20) : userLogonName;
            else
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") No username set for " + member.FirstName + " " + member.LastName + ", so can't continue.");
                return;
            }

            // Hypothetically they should always have an email, but let's be
            // careful anyway
            if (member.Email != null && member.Email.Length > 0)
                // Can only use the first 256 characters (though never seen an
                // email address that long, but okay....)
                userPrincipal.UserPrincipalName = member.Email.Length > 256 ? member.Email.Substring(0, 256) : member.Email;

            // The user may have an RFID tag
            // We want to make sure there are no tags for the user
            //resetRFIDField(userPrincipal);
            // Now we add any tags
            /*
            FieldValue rfidTagFV = getValueForKey(member, "RFID Tag");
            if (rfidTagFV != null && rfidTagFV.ToString().Length > 0)
            {
                string rfidTag = (string)rfidTagFV.Value;
                addRFIDTag(userPrincipal, rfidTag.Trim()); 
            }
            */

            // Generate a useless password that the user doesn't know so
            // he or she must create a new one.
            String pwdOfNewlyCreatedUser = RandomString(15) + "!?";
            userPrincipal.SetPassword(pwdOfNewlyCreatedUser);
            userPrincipal.PasswordNotRequired = false;

            userPrincipal.Enabled = true;
            userPrincipal.ExpirePasswordNow();

            // Set the "Employee ID" to the WA member ID
            userPrincipal.EmployeeId = member.Id.ToString();

            try
            {
                userPrincipal.Save();
                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") Created a new user for " + member.FirstName + " " + member.LastName + " AD username: " + (string)member.FieldValues[FieldValue.ADUSERNAME].Value);
            }
            catch (Exception e)
            {
                Log.Write(Log.Level.Error, "(mid:" + member.Id + ") Exception creating user object for " + member.FirstName + " " + member.LastName + " with AD username " + (string)member.FieldValues[FieldValue.ADUSERNAME].Value + " -> " + e);
            }
        }

        // If the member is enabled, they should be in the ADUsersOU OU, otherwise
        // they should be in the ADInactiveUsersOU
        private void moveUserToGroup(ref UserPrincipal userPrincipal, bool shouldBeEnabled)
        {
            // Two flavors if we're in the domain, or not in the domain. There's enough differences
            // in the function calls that it's just as well to have two separate blocks of code with
            // nothing shared
            if (this.username.Length > 0)
            {
                // We are *not* in the domain, so we need to build the full LDAP URL
                // which includes the server IP for this to work (also note the use
                // of login credentials below as well)
                string userCN = String.Format("LDAP://{0}/{1}", this.adServer, userPrincipal.DistinguishedName);
                string newOUForUser = String.Format("LDAP://{0}/{1}", this.adServer, shouldBeEnabled ? this.membersPath : this.inactiveMembersPath);

                DirectoryEntry currentOU = new DirectoryEntry(userCN, this.username, this.password);
                DirectoryEntry newOU = new DirectoryEntry(newOUForUser, this.username, this.password);
                // And now actually move the user to the new OU
                currentOU.MoveTo(newOU);
                newOU.Close();
                currentOU.Close();
            }
            else
            {
                // We are in the domain with a user that can do everything
                // implicitly
                string userCN = String.Format("LDAP://{0}", userPrincipal.DistinguishedName);
                string newOUForUser = String.Format("LDAP://{0}", shouldBeEnabled ? this.membersPath : this.inactiveMembersPath);

                DirectoryEntry currentOU = new DirectoryEntry(userCN);
                DirectoryEntry newOU = new DirectoryEntry(newOUForUser);
                // And now actually move the user to the new OU
                currentOU.MoveTo(newOU);
                newOU.Close();
                currentOU.Close();
            }            
        }

        // The boolean is to indicate whether the member is enabled or not
        // (used to set the appropriate status in B2C)
        private bool UpdateUser(Member member, ref UserPrincipal userPrincipal)
        {
            // They may have updated their email address
            if (userPrincipal.UserPrincipalName != member.Email && (member.Email != null && member.Email.Length > 0))
            {
                // Can only use the first 256 characters (though never seen an
                // email address that long, but okay....)
                userPrincipal.UserPrincipalName = member.Email.Length > 256 ? member.Email.Substring(0, 256) : member.Email;
            }

            // And do the same for the mail field
            if (userPrincipal.EmailAddress != member.Email && (member.Email != null && member.Email.Length > 0))
            {
                // Can only use the first 256 characters (though never seen an
                // email address that long, but okay....)
                userPrincipal.EmailAddress = member.Email.Length > 256 ? member.Email.Substring(0, 256) : member.Email;
            }

            // The user may have updated their RFID tag, so first we
            // reset the values in AD
            resetRFIDField(userPrincipal);
            // Now, having wiped out the previous value, let's see if there
            // are any tags
            FieldValue rfidTagFV = getValueForKey(member, "RFID Tag");
            if (rfidTagFV != null && rfidTagFV.Value != null && rfidTagFV.ToString().Length > 0)
            {
                // Split on a comma
                string[] tokens = ((string)rfidTagFV.Value).Split(',');
                foreach (var rfidTag in tokens)
                {                 
                    // Add the tag, but make sure there aren't any spaces around it
                    addRFIDTag(userPrincipal, rfidTag.Trim());
                }
            }

            // Set the "Employee ID" to the WA member ID
            userPrincipal.EmployeeId = member.Id.ToString();
            
            // And update their group memberships
            addUserToGroups("Computer Authorizations", ref userPrincipal, member);
            // And do the same thing for the other authorizations (e.g. welders, lathe, etc.)
            addUserToGroups("Authorizations", ref userPrincipal, member);

            // And now we determine whether the user is active or lapsed (set via WA), and
            // we put the user in the appropriate OU based on that
            bool isCurrentlyEnabled = (bool)userPrincipal.Enabled;
            bool shouldBeEnabled = member.Status == "Lapsed" ? false : true;
            Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") " + member.FirstName + " " + member.LastName + " is currenty set to " + (shouldBeEnabled ? "ENABLED" : "DISABLED"));

            // We also need to check three other things, whether they've signed
            // the extra essentials waiver, whether they've completed orientation,
            // and whether they're disabled via the master "disabled" switch
            var efc = getValueForKey(member, "Essentials Form Completed");
            var oc = getValueForKey(member, "Orientation Completed");
            /*
            if (efc.Value == null || oc.Value == null)
            {
                Log.Write(Log.Level.Warning, "(mid:"+member.Id+") (forms) We are explicitly disabling " + member.FirstName + " " + member.LastName);
                shouldBeEnabled = false;
            }
            */

            // 1/13/22 - If the vaxx field isn't set, or is set to "Not Validated" we disable the member,
            // but so they still have access to online stuff, we use the reEnableForOnlineAccess flag
            // to make sure they can still log into things like Canvas, etc.
            bool reEnableForOnlineAccess = false;
            var vaxx = getValueForKey(member, "2022 Covid Vaccine Policy Compliance");
            if (vaxx.Value == null)
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") (vaxx) We are explicitly disabling " + member.FirstName + " " + member.LastName);
                shouldBeEnabled = false;
                // ...but allow them to have access to online stuff...
                reEnableForOnlineAccess = true;
            }
            else 
            {
                JObject mksVal = JObject.Parse(vaxx.Value.ToString());

                var isValidated = (string)mksVal.GetValue("Label");
                if (isValidated == "Not Validated")                
                {
                    Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") (vaxx) We are explicitly disabling " + member.FirstName + " " + member.LastName);
                    shouldBeEnabled = false;
                    // ...but allow them to have access to online stuff...
                    reEnableForOnlineAccess = true;
                }
            }
           
            // The member is disabled if the field is not null and explicitly
            // set to Yes
            var mks = getValueForKey(member, "Disabled");
            var mustDisable = false;
            if (mks.Value != null)
            {
                JObject mksVal = JObject.Parse(mks.Value.ToString());

                var isDisabled = (string)mksVal.GetValue("Label");
                if (isDisabled == "Yes")
                {
                    mustDisable = true;
                }
            }

            // If the user has been explicitly disabled in WA, they're disabled, no
            // matter what
            if (mustDisable == true)
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") (must disable) We are explicitly disabling " + member.FirstName + " " + member.LastName);
                shouldBeEnabled = false;
                // If we really must disable the user, then we will make sure that
                // we don't also enable online stuff
                reEnableForOnlineAccess = false;
            }

            // Last check of membership status
            if (member.MembershipEnabled == false)
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") " + member.FirstName + " " + member.LastName + "'s membership is *NOT* enabled in WA, so we're completing disabling them.");

                shouldBeEnabled = false;
                // And since they're really disabled, we will not enable
                // online access
                reEnableForOnlineAccess = false;
            }

            if (isCurrentlyEnabled != shouldBeEnabled)
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") Going to set " + member.FirstName + " " + member.LastName + "'s status to " + (shouldBeEnabled ? "enabled" : "disabled"));
                userPrincipal.Enabled = shouldBeEnabled;
                userPrincipal.Save();
                // And put the person in the right OU
                //moveUserToGroup(ref userPrincipal, shouldBeEnabled);
            }
            

            // And we're done modifying the user, so let's just save our remaining changes
            try
            {
                userPrincipal.Save();
                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") Updated user " + member.FirstName + " " + member.LastName);
            }
            catch (Exception e)
            {
                Log.Write(Log.Level.Error, "(mid:" + member.Id + ") Exception when updating user object. " + e);
            }

            // If we disabled the user in AD for vaxx reasons, we return *true* here
            // so they still have access to online resources
            if (reEnableForOnlineAccess)
            {
                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") (vaxx) Online access allowed for " + member.FirstName + " " + member.LastName);
                return true;
            }

            return shouldBeEnabled;
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
            this.username = MyIni.Read("ADUser").Trim(); 
            this.password = MyIni.Read("ADPassword").Trim();
            // The AD server name or IP address
            this.adServer = MyIni.Read("ADIPAddress").Trim();
            // The LDAP path to the users
            // (e.g. CN=users,DC=ad,DC=organizationname,DC=org)
            this.membersPath = MyIni.Read("ADUsersOU").Trim();
            // And this is for the inactive users
            this.inactiveMembersPath = MyIni.Read("ADInactiveUsersOU").Trim();

            // If we don't have a CN, that's bad because we really need that one
            if (this.membersPath.Length == 0)
            {
                Log.Write(Log.Level.Error, "WHOA! The CN needs to be set in the ini file! (The ADUsersOU property). Not going to continue because I don't know where to put anything!");                
                return;
            }
            else
            {
                Log.Write(Log.Level.Informational, string.Format("Going to work with member objects in {0}", this.membersPath));                
            }

            if (this.inactiveMembersPath.Length == 0)
            {
                Log.Write(Log.Level.Error, "WHOA! The Inactive CN needs to be set in the ini file! (The ADInactiveUsersOU property). Not going to continue because I don't know where to put anything!");                
                return;
            }
            else
            {
                Log.Write(Log.Level.Informational, string.Format("Going to work with inactive member objects in {0}", this.inactiveMembersPath));                
            }

            // If we have a user/password/IP combo, then we'll assume
            // we're currently running on a machine that is *not* on the
            // domain we want to work with.
            if (this.username.Length == 0 || this.password.Length == 0 || this.adServer.Length == 0)
            {
                Log.Write(Log.Level.Informational, "Ok, we're going to connect assuming we're on the domain, run by a user with appropriate permissions");
                // We need to use this context so we have full access to the domain, and not just one part of it
                this.pc = new PrincipalContext(ContextType.Domain);
            }
            else
            {
                // We have all the credentials, so we're going to try to connect using those
                Log.Write(Log.Level.Informational, "Going to connect with credentials...");
                try
                {                    
                    this.pc = new PrincipalContext(ContextType.Domain, this.adServer, this.username, this.password);               
                }
                catch (Exception e)
                {
                    Log.Write(Log.Level.Error, string.Format("Hmm, failed to create PrincipalContext. Exception is: {0}", e));
                }
            }
        }

        public void HandleMember(Member member)
        {
            // Is this a real member, or just a contact?
            if (member.MembershipLevel == null)
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") This person is not a member!");
                return;
            }

            // But do nothing if the membership is still pending
            if (member.Status == "PendingNew")
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") Ah, but membership is still pending, so not going to add");
                return;
            }

            UserPrincipal u = new UserPrincipal(pc)
            {
                SamAccountName = (string)member.FieldValues[FieldValue.ADUSERNAME].Value
            };

            if (FindExistingUser(ref u)) 
            {
                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") Oh, hey, found " + member.FirstName + " in AD");
                bool isMemberEnabled = UpdateUser(member, ref u);

                this.b2cActions.UpdateUser(isMemberEnabled, member, u);
            }
            else
            {
                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") Didn't find " + member.FirstName + " in AD, so must be new...");
                CreateUser(member);

                // Now we need to get the AD object so we can update B2C with it
                UserPrincipal newU = new UserPrincipal(pc)
                {
                    SamAccountName = (string)member.FieldValues[FieldValue.ADUSERNAME].Value
                };

                if (FindExistingUser(ref newU) == false)
                {
                    Log.Write(Log.Level.Error, "(mid:" + member.Id + ") " + string.Format("Hmm, for {0} {1} we just created the username {2} in AD, but couldn't find it", member.FirstName, member.LastName, (string)member.FieldValues[FieldValue.ADUSERNAME].Value));
                                       
                    // And we're not gonna continue
                    return;
                }

                this.b2cActions.AddNewUser(newU, member.Id);
            }           
        }
    }
}
