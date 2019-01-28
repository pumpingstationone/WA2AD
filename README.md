# WA2AD
C#-based connector from Wild Apricot to Active Directory 

## Purpose
This program was written to synchronize membership accounts using [Wild Apricot](http://wildapricot.org) and Microsoft's Active Directory domain system. The assumption is that the Wild Apricot database is the source of truth and that users should be created or disabled based on their Wild Apricot status.

### Project
This project makes use of [Json.Net](https://www.newtonsoft.com/json) which can be installed via NuGet. For Active Directory it uses `System.DirectoryServices` and is targeted to .NET Framework 4.6.1.

### Files
#### `ADActions.cs`
This file contains all the Active Directory-specific code. The object is instantiated as a member of `Program` in *Program.cs* and initiates a connection to the AD server in its constructor. 

Currently it creates a user, and if the user exists, checks whether the user should be disabled and if so, disables the user's account in AD.

#### `Program.cs`
The main class of the program. That's what VS 2017 used as a default and figured not to bother changing it.

#### `WAData.cs`

Retrieves the contacts database using Wild Apricot's v2 API. Note that you need to provide an Application API key that is unique to both your application and WA account.

#### `WAObjects.cs`

The file contains the classes for working with the WA data. 

#### `WA2AD.ini`

This is the settings file. You need to at the very least populate the `WAToken` field with the appropriate security token from Wild Apricot for the app. The other fields are necessary only when running the application from a machine that is not in the actual domain you're working with.
