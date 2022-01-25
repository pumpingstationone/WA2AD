﻿using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.Threading.Tasks;
using WildApricotAPI;

namespace WA2AD
{
    class B2CActions
    {
        internal class B2cCustomAttributeHelper
        {
            internal readonly string _b2cExtensionAppClientId;

            internal B2cCustomAttributeHelper(string b2cExtensionAppClientId)
            {
                _b2cExtensionAppClientId = b2cExtensionAppClientId.Replace("-", "");
            }

            internal string GetCompleteAttributeName(string attributeName)
            {
                if (string.IsNullOrWhiteSpace(attributeName))
                {
                    throw new System.ArgumentException("Parameter cannot be null", nameof(attributeName));
                }

                return $"extension_{_b2cExtensionAppClientId}_{attributeName}";
            }
        }
        
        private string tenantId { get; set; }
        private string appId { get; set; }
        private string clientSecret { get; set; }
        private string b2cExtensionAppClientId { get; set; }

        // The objects we need to work with B2C
        private IConfidentialClientApplication confidentialClientApplication;
        private ClientCredentialProvider authProvider;
        private GraphServiceClient graphClient;

        private void initializeB2C()
        {
            this.confidentialClientApplication = ConfidentialClientApplicationBuilder
              .Create(this.appId)
              .WithTenantId(this.tenantId)
              .WithClientSecret(this.clientSecret)
              .Build();
            this.authProvider = new ClientCredentialProvider(confidentialClientApplication);

            this.graphClient = new GraphServiceClient(authProvider);
        }

        private string getB2CAttributeNameFor(string attributeName)
        {
            string longName = "";

            B2cCustomAttributeHelper helper = new B2cCustomAttributeHelper(this.b2cExtensionAppClientId);
            longName = helper.GetCompleteAttributeName(attributeName);

            return longName;
        }

        private async Task<bool> ReallyAddNewUser(UserPrincipal u, int waID)
        {
            // All our custom attributes
            IDictionary<string, object> extensionInstance = new Dictionary<string, object>();
            extensionInstance.Add(getB2CAttributeNameFor("CRMNumber"), waID.ToString());
            extensionInstance.Add(getB2CAttributeNameFor("ADObjectGUID"), u.Guid.ToString());
            // These are only set during creation
            extensionInstance.Add(getB2CAttributeNameFor("PasswordMigrationComplete"), false);
            extensionInstance.Add(getB2CAttributeNameFor("AccountActivated"), false);

            var result = await graphClient.Users
                .Request()
                .AddAsync(new User
                {
                    AccountEnabled = true,
                    GivenName = u.GivenName,
                    Surname = u.Surname,
                    DisplayName = string.Format("{0} {1}", u.GivenName, u.Surname),
                    Identities = new List<ObjectIdentity>
                    {
                            new ObjectIdentity()
                            {
                                SignInType = "emailAddress",
                                Issuer = tenantId,
                                IssuerAssignedId = u.EmailAddress

                            },
                            new ObjectIdentity()
                            {
                                SignInType = "userName",
                                Issuer = tenantId,
                                IssuerAssignedId = u.Name

                            }
                    },
                    PasswordProfile = new PasswordProfile()
                    {
                        Password = "..Nothing123!"
                    },
                    PasswordPolicies = "DisablePasswordExpiration",
                    Mail = u.EmailAddress,
                    AdditionalData = extensionInstance
                });

            string userId = result.Id;

            return true;
        }

        public void AddNewUser(UserPrincipal u, int waID)
        {
            try
            {
                Task<bool> task = ReallyAddNewUser(u, waID);
                bool result = task.Result;
            }
            catch (Exception ex)
            {
                Log.Write(Log.Level.Error, "(mid:" + waID + ") " + string.Format("Drat, got {0} when trying to create the B2C user for {1}", ex.Message, u.Name));
            }

            Log.Write(Log.Level.Informational, "(mid:" + waID + ") " + string.Format("Created the user {0} in B2C", u.Name));
        }

        async Task<User> FindUserByADGuid(string userADGuid)
        {
            B2cCustomAttributeHelper helper = new B2cCustomAttributeHelper(this.b2cExtensionAppClientId);
            string adObjectGuidAttributeName = helper.GetCompleteAttributeName("ADObjectGUID");
           
            try
            {
                // Get user by sign-in name
                var result = await this.graphClient.Users
                    .Request()
                    .Filter($"{adObjectGuidAttributeName} eq '{userADGuid}'")
                    .Select($"id,givenName,surName,displayName,mail,identities")
                    .GetAsync();

                if (result != null)
                {
                    // Yay, we found the user
                    Log.Write(Log.Level.Informational, string.Format("We found {0}", userADGuid));

                    return result[0];
                }
            }
            catch (Exception ex)
            {                
                // Note that we'll be in this block if the person isn't in B2C,
                // which could be because they're new. So this message is a warning,
                // not an error
                Log.Write(Log.Level.Warning, string.Format("Drat, got {0} when trying to find the B2C user with AD guid {1}, which is to be expected if they're new", ex.Message, userADGuid));
            }

            return null;
        }

        // Member is the WA data
        // UserPrincipal is the AD data
        // User is the existing B2C data
        private async Task<bool> ReallyUpdateUser(bool isMemberEnabled, Member member, UserPrincipal u, User user)
        {
            // Active/Inactive status
            user.AccountEnabled = isMemberEnabled;

            // And if the name changed...
            user.GivenName = u.GivenName;
            user.Surname = u.Surname;
            user.DisplayName = string.Format("{0} {1}", u.GivenName, u.Surname);

            // Here we're changing the email address, both the
            // mail field as well as the email-based identity
            // (the two should always be in sync)
            user.Mail = u.EmailAddress;

            List<ObjectIdentity> identities = (List<ObjectIdentity>)user.Identities;
            foreach (ObjectIdentity oi in identities)
            {
                if (oi.SignInType == "emailAddress")
                {
                    oi.IssuerAssignedId = u.EmailAddress;
                }
            }

            // And now do the actual update
            try
            {
                // Update user by object ID
                await graphClient.Users[user.Id]
                   .Request()
                   .UpdateAsync(user);

                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") " + "User with object ID " + user.Id + " successfully updated.");

                return true;
            }
            catch (Exception ex)
            {               
                Log.Write(Log.Level.Error, "(mid:" + member.Id + ") " + string.Format("Drat, got {0} when trying to really update the B2C user for {1}", ex.Message, u.Name));
            }

            return false;
        }

        public void UpdateUser(bool isMemberEnabled, Member member, UserPrincipal u)
        {
            // This method checks to see if the user exists in B2C and if not, adds the user,
            // otherwise updates
            bool noErrors = true;
            try
            {
                Task<User> findUserTask = FindUserByADGuid(u.Guid.ToString());
                User existingUser = findUserTask.Result;
                if (existingUser == null)
                {
                    // User isn't in B2C, so we add it now if the member is enabled
                    if (isMemberEnabled == true)
                    {                        
                        Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") " + string.Format("{0} is not in B2C, so we're going to add it now", u.Name));                        
                        AddNewUser(u, member.Id);
                    }
                }
                else
                {
                    // Oh, hey, we found the user, so we'll see if we
                    // need to do any updates                    
                    Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") " + string.Format("{0} is in B2C, so we're going to do any updates", u.Name));                    

                    Task<bool> updateTask = ReallyUpdateUser(isMemberEnabled, member, u, existingUser);
                    bool okay = updateTask.Result;
                }
            }
            catch (Exception ex)
            {
                Log.Write(Log.Level.Error, "(mid:" + member.Id + ") " + string.Format("Drat, got {0} when trying to update the B2C user for {1}", ex.Message, u.Name));
                noErrors = false;
            }

            if (noErrors)
            {
                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") " + string.Format("Updated the user {0} in B2C", u.Name));
            }
            else
            {
                Log.Write(Log.Level.Warning, "(mid:" + member.Id + ") " + string.Format("Hey! Check the user {0} in B2C", u.Name));
            }
        }

        public B2CActions()
        {          
            // Get our token from the ini file
            var MyIni = new IniFile();
            this.tenantId = MyIni.Read("TenantId").Trim();
            if (this.tenantId.Length == 0)
            {
                Log.Write(Log.Level.Error, "Whoops, can't get the B2C Tenant ID! Check the ini file is in the same dir as the executable and set properly!");
                return;
            }

            // Assume we can get the rest of them
            this.appId = MyIni.Read("AppId").Trim();
            this.clientSecret = MyIni.Read("ClientSecret").Trim();
            this.b2cExtensionAppClientId = MyIni.Read("B2cExtensionAppClientId").Trim();

            initializeB2C();
        }
    }
}
