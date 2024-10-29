using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace WinKiosk.CustomSetup
{
    [RunInstaller(true)]
    public class WinKioskCustomInstaller : Installer
    {
        // Import necessary functions from Netapi32.dll
        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint NetUserAdd(
            [MarshalAs(UnmanagedType.LPWStr)] string servername,
            uint level,
            ref USER_INFO_1 buf,
            out uint parm_err);

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint NetLocalGroupAddMembers(
            [MarshalAs(UnmanagedType.LPWStr)] string servername,
            [MarshalAs(UnmanagedType.LPWStr)] string groupname,
            uint level,
            [In] LOCALGROUP_MEMBERS_INFO_3[] buf,
            uint totalentries);

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint NetUserDel(
            [MarshalAs(UnmanagedType.LPWStr)] string servername,
            [MarshalAs(UnmanagedType.LPWStr)] string username);

        // Import DeleteProfile from userenv.dll
        [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DeleteProfile(
            [MarshalAs(UnmanagedType.LPWStr)] string sidString,
            [MarshalAs(UnmanagedType.LPWStr)] string profilePath,
            [MarshalAs(UnmanagedType.LPWStr)] string computerName);

        // Structures for user and group information
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct USER_INFO_1
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string usri1_name;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string usri1_password;
            public uint usri1_password_age;
            public uint usri1_priv;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string usri1_home_dir;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string usri1_comment;
            public uint usri1_flags;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string usri1_script_path;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LOCALGROUP_MEMBERS_INFO_3
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string DomainAndName;
        }

        // Constants for user privileges and flags
        private const uint USER_PRIV_USER = 1;
        private const uint UF_SCRIPT = 0x0001;
        private const uint UF_DONT_EXPIRE_PASSWD = 0x10000;

        // Import functions for loading and unloading user profiles
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out IntPtr phToken);

        [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LoadUserProfile(
            IntPtr hToken,
            ref PROFILEINFO lpProfileInfo);

        [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UnloadUserProfile(
            IntPtr hToken,
            IntPtr hProfile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROFILEINFO
        {
            public int dwSize;
            public int dwFlags;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpUserName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpProfilePath;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpDefaultPath;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpServerName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpPolicyPath;
            public IntPtr hProfile;
        }

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint FormatMessage(uint dwFlags, IntPtr lpSource, uint dwMessageId, uint dwLanguageId, ref IntPtr lpBuffer, uint nSize, IntPtr Arguments);

        // Constants for Auto-Logon registry keys
        private const string WINLOGON_REG_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private const string AUTO_ADMIN_LOGON = "AutoAdminLogon";
        private const string DEFAULT_USERNAME = "DefaultUsername";
        private const string DEFAULT_PASSWORD = "DefaultPassword";
        private const string DEFAULT_DOMAIN_NAME = "DefaultDomainName";
        // Removed ForceAutoLogon as it forces auto-logon even after logout

        // Constructor
        public WinKioskCustomInstaller()
        {
        }

        // Override the Install method to execute actions during installation
        public override void Install(IDictionary stateSaver)
        {
            base.Install(stateSaver);

            try
            {
                // Retrieve parameters from CustomActionData
                string targetDir = Context.Parameters["targetDir"];
                string username = Context.Parameters["USERNAME"];
                string password = Context.Parameters["PASSWORD"];

                // Set default values if necessary
                if (string.IsNullOrEmpty(username))
                {
                    username = "Kiosk";
                }

                if (string.IsNullOrEmpty(password))
                {
                    password = "kiosk";
                }

                // Clean targetDir
                targetDir = targetDir.TrimEnd('\\', ' ');

                // Log retrieved values
                LogEvent($"Install - targetDir: '{targetDir}', username: '{username}', password: '{password}'");

                // Store the username in the registry for later use during uninstallation
                StoreUsernameInRegistry(username);

                // Create the user
                CreateUser(targetDir, username, password);

                // Enable Auto-Logon for the new user
                EnableAutoLogon(username, password);
            }
            catch (Exception ex)
            {
                LogEvent($"Exception in Install: {ex.Message}", EventLogEntryType.Error);
                throw new InstallException("Installation failed: " + ex.Message, ex);
            }
        }

        // Override the Uninstall method to execute actions during uninstallation
        public override void Uninstall(IDictionary savedState)
        {
            base.Uninstall(savedState);

            try
            {
                // Read the username from the registry
                string username = ReadUsernameFromRegistry();

                if (string.IsNullOrEmpty(username))
                {
                    username = "Kiosk";
                }

                // Delete the user
                DeleteUser(username);

                // Disable Auto-Logon during uninstallation
                DisableAutoLogon();
            }
            catch (Exception ex)
            {
                LogEvent($"Exception in Uninstall: {ex.Message}", EventLogEntryType.Error);
                // Do not throw an exception to avoid interrupting the uninstallation
            }

            // Remove the username from the registry
            RemoveUsernameFromRegistry();
        }

        // Method to create the user
        private void CreateUser(string targetDir, string username, string password)
        {
            try
            {
                LogEvent($"CreateUser - username: {username}, password: {password}");

                // Create the local user account
                CreateLocalUser(username, password);

                // Get the localized name of the "Users" group
                string usersGroupName = GetUsersGroupName();
                LogEvent($"CreateUser - usersGroupName: {usersGroupName}");

                // Add the user to the "Users" group
                AddUserToGroup(username, usersGroupName);

                // Get the user's SID
                string sidString = GetUserSid(username);
                LogEvent($"CreateUser - sidString: {sidString}");

                // Modify the registry to set a custom shell and disable Ctrl+Alt+Del options
                SetCustomShellAndPolicies(username, password, sidString, targetDir);
            }
            catch (Exception ex)
            {
                // Log and throw an installation exception
                LogEvent($"Error in CreateUser: {ex.Message}", EventLogEntryType.Error);
                throw new InstallException("Error creating user: " + ex.Message, ex);
            }
        }

        // Method to create a local user
        private void CreateLocalUser(string username, string password)
        {
            LogEvent($"CreateLocalUser - username: {username}, password: {password}");

            USER_INFO_1 userInfo = new USER_INFO_1
            {
                usri1_name = username,
                usri1_password = password,
                usri1_priv = USER_PRIV_USER,
                usri1_home_dir = null,
                usri1_comment = "Created by installer",
                usri1_flags = UF_SCRIPT | UF_DONT_EXPIRE_PASSWD,
                usri1_script_path = null
            };

            uint paramError = 0;

            // Call NetUserAdd to create the user account
            uint result = NetUserAdd(null, 1, ref userInfo, out paramError);

            LogEvent($"CreateLocalUser - NetUserAdd result: {result}, paramError: {paramError}");

            if (result != 0)
            {
                // Throw an exception with the error message if user creation fails
                string message = GetErrorMessage(result);
                LogEvent($"CreateLocalUser - Error: {message}", EventLogEntryType.Error);
                throw new Exception($"Failed to create user. NetUserAdd error code: {result}. Parameter error: {paramError}. Message: {message}");
            }
        }

        // Method to get the localized name of the "Users" group
        private string GetUsersGroupName()
        {
            SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            NTAccount account = sid.Translate(typeof(NTAccount)) as NTAccount;
            string groupName = account.Value.Split('\\')[1];
            LogEvent($"GetUsersGroupName - groupName: {groupName}");
            return groupName;
        }

        // Method to add the user to a local group
        private void AddUserToGroup(string username, string groupname)
        {
            LogEvent($"AddUserToGroup - username: {username}, groupname: {groupname}");

            // Get the machine name
            string machineName = Environment.MachineName;
            string qualifiedUsername = $"{machineName}\\{username}";

            LOCALGROUP_MEMBERS_INFO_3[] members = new LOCALGROUP_MEMBERS_INFO_3[1];
            members[0].DomainAndName = qualifiedUsername;

            LogEvent($"AddUserToGroup - Qualified Username: {qualifiedUsername}");

            uint result = NetLocalGroupAddMembers(null, groupname, 3, members, 1);

            LogEvent($"AddUserToGroup - NetLocalGroupAddMembers result: {result}");

            if (result != 0)
            {
                string message = GetErrorMessage(result);
                LogEvent($"AddUserToGroup - Error: {message}", EventLogEntryType.Error);
                throw new Exception($"Failed to add user to group. Error code: {result}. Message: {message}");
            }
            else
            {
                LogEvent($"AddUserToGroup - User '{qualifiedUsername}' added to group '{groupname}' successfully.");
            }
        }

        // Method to get the SID of a user
        private string GetUserSid(string username)
        {
            LogEvent($"GetUserSid - username: {username}");

            NTAccount ntAccount = new NTAccount(username);
            SecurityIdentifier sid = (SecurityIdentifier)ntAccount.Translate(typeof(SecurityIdentifier));
            string sidString = sid.ToString();

            LogEvent($"GetUserSid - sidString: {sidString}");

            return sidString;
        }

        // Method to set a custom shell and policies
        private void SetCustomShellAndPolicies(string username, string password, string sidString, string targetDir)
        {
            LogEvent($"SetCustomShellAndPolicies - sidString: {sidString}, targetDir: '{targetDir}'");

            IntPtr userToken = IntPtr.Zero;
            PROFILEINFO profileInfo = new PROFILEINFO();

            try
            {
                // Log on the user to obtain a token
                bool loggedOn = LogonUser(
                    username,
                    Environment.MachineName,
                    password,
                    2, // LOGON32_LOGON_INTERACTIVE
                    0, // LOGON32_PROVIDER_DEFAULT
                    out userToken);

                if (!loggedOn)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"LogonUser failed. Error code: {error}");
                }

                // Load the user's profile
                profileInfo.dwSize = Marshal.SizeOf(typeof(PROFILEINFO));
                profileInfo.lpUserName = username;
                profileInfo.dwFlags = 1; // PI_NOUI

                bool profileLoaded = LoadUserProfile(userToken, ref profileInfo);
                if (!profileLoaded)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"LoadUserProfile failed. Error code: {error}");
                }

                // Registry path for the user's Winlogon settings
                string regPath = sidString + @"\Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
                LogEvent($"SetCustomShellAndPolicies - regPath: {regPath}");

                using (RegistryKey usersKey = Registry.Users)
                {
                    using (RegistryKey winlogonKey = usersKey.CreateSubKey(regPath))
                    {
                        if (winlogonKey != null)
                        {
                            // Full path to your application
                            string appPath = System.IO.Path.Combine(targetDir, "WinKiosk.Core.exe");
                            LogEvent($"SetCustomShellAndPolicies - appPath: {appPath}");

                            // Check if the file exists
                            if (!System.IO.File.Exists(appPath))
                            {
                                LogEvent($"SetCustomShellAndPolicies - File {appPath} does not exist.", EventLogEntryType.Error);
                                throw new Exception($"File {appPath} does not exist.");
                            }

                            // Set the 'Shell' value to your application's path
                            winlogonKey.SetValue("Shell", appPath, RegistryValueKind.String);
                            LogEvent("SetCustomShellAndPolicies - 'Shell' value set successfully.");

                            // Disable Ctrl+Alt+Del options
                            SetCtrlAltDelPolicies(usersKey, sidString);
                        }
                        else
                        {
                            // Throw an exception if the registry key cannot be created or opened
                            string errorMessage = "Failed to create or open registry key: " + regPath;
                            LogEvent($"SetCustomShellAndPolicies - Error: {errorMessage}", EventLogEntryType.Error);
                            throw new Exception(errorMessage);
                        }
                    }
                }
            }
            finally
            {
                // Unload the user's profile
                if (userToken != IntPtr.Zero && profileInfo.hProfile != IntPtr.Zero)
                {
                    UnloadUserProfile(userToken, profileInfo.hProfile);
                }

                // Close the user token handle
                if (userToken != IntPtr.Zero)
                {
                    CloseHandle(userToken);
                }
            }
        }

        // Method to set Ctrl+Alt+Del policies
        private void SetCtrlAltDelPolicies(RegistryKey usersKey, string sidString)
        {
            string systemPolicyPath = sidString + @"\Software\Microsoft\Windows\CurrentVersion\Policies\System";

            try
            {
                using (RegistryKey systemKey = usersKey.CreateSubKey(systemPolicyPath))
                {
                    if (systemKey != null)
                    {
                        // Disable "Lock Workstation"
                        systemKey.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
                        LogEvent("SetCtrlAltDelPolicies - DisableLockWorkstation set to 1.");

                        // Hide "Change user"
                        systemKey.SetValue("HideFastUserSwitching", 1, RegistryValueKind.DWord);
                        LogEvent("SetCtrlAltDelPolicies - HideFastUserSwitching set to 1.");

                        // Disable "Change password"
                        systemKey.SetValue("DisableChangePassword", 1, RegistryValueKind.DWord);
                        LogEvent("SetCtrlAltDelPolicies - DisableChangePassword set to 1.");

                        // Disable Task Manager if not already set
                        if (systemKey.GetValue("DisableTaskMgr") == null)
                        {
                            systemKey.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                            LogEvent("SetCtrlAltDelPolicies - DisableTaskMgr set to 1.");
                        }
                    }
                    else
                    {
                        LogEvent($"SetCtrlAltDelPolicies - Unable to create or open registry key: {systemPolicyPath}", EventLogEntryType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent($"SetCtrlAltDelPolicies - Error: {ex.Message}", EventLogEntryType.Error);
                // Do not throw an exception to avoid interrupting the installation
            }
        }

        // Method to delete the user
        private void DeleteUser(string username)
        {
            LogEvent($"DeleteUser - username: {username}");

            // Delete the user account
            uint result = NetUserDel(null, username);

            LogEvent($"DeleteUser - NetUserDel result: {result}");

            if (result != 0)
            {
                string message = GetErrorMessage(result);
                LogEvent($"DeleteUser - Error: {message}", EventLogEntryType.Error);
                // Do not throw an exception to continue uninstallation
            }
            else
            {
                LogEvent("DeleteUser - User deleted successfully.");

                // Delete the user's profile
                DeleteUserProfile(username);
            }
        }

        // Method to delete the user's profile
        private void DeleteUserProfile(string username)
        {
            LogEvent($"DeleteUserProfile - username: {username}");

            try
            {
                // Get the user's SID
                string sidString = GetUserSid(username);

                LogEvent($"DeleteUserProfile - sidString: {sidString}");

                // Delete the user profile
                bool result = DeleteProfile(sidString, null, null);

                if (!result)
                {
                    int error = Marshal.GetLastWin32Error();
                    string message = GetErrorMessage((uint)error);
                    LogEvent($"DeleteUserProfile - Error: {message}", EventLogEntryType.Error);
                    // Do not throw an exception to continue uninstallation
                }
                else
                {
                    LogEvent("DeleteUserProfile - User profile deleted successfully.");
                }
            }
            catch (Exception ex)
            {
                LogEvent($"DeleteUserProfile - Exception: {ex.Message}", EventLogEntryType.Error);
            }
        }

        // Method to get the error message corresponding to the error code
        private string GetErrorMessage(uint errorCode)
        {
            const uint FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x00000100;
            const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;

            IntPtr lpMsgBuf = IntPtr.Zero;
            uint dwFlags = FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM;

            uint dwChars = FormatMessage(dwFlags, IntPtr.Zero, errorCode, 0, ref lpMsgBuf, 0, IntPtr.Zero);

            if (dwChars == 0)
            {
                return $"Unknown error code: {errorCode}";
            }
            else
            {
                string message = Marshal.PtrToStringUni(lpMsgBuf);
                Marshal.FreeHGlobal(lpMsgBuf);
                return message.Trim();
            }
        }

        // Method to store the username in the registry
        private void StoreUsernameInRegistry(string username)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\WinKiosk"))
                {
                    if (key != null)
                    {
                        key.SetValue("Username", username, RegistryValueKind.String);
                        LogEvent($"StoreUsernameInRegistry - Username '{username}' stored in registry.");
                    }
                    else
                    {
                        LogEvent("StoreUsernameInRegistry - Unable to create or open registry key SOFTWARE\\WinKiosk.", EventLogEntryType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent($"StoreUsernameInRegistry - Error: {ex.Message}", EventLogEntryType.Error);
                // Do not throw an exception to avoid interrupting the installation
            }
        }

        // Method to read the username from the registry
        private string ReadUsernameFromRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinKiosk"))
                {
                    if (key != null)
                    {
                        object usernameObj = key.GetValue("Username");
                        if (usernameObj != null)
                        {
                            string username = usernameObj.ToString();
                            LogEvent($"ReadUsernameFromRegistry - Username '{username}' read from registry.");
                            return username;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent($"ReadUsernameFromRegistry - Error: {ex.Message}", EventLogEntryType.Error);
            }

            return null; // Return null if reading fails
        }

        // Method to remove the username from the registry
        private void RemoveUsernameFromRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinKiosk", writable: true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("Username", throwOnMissingValue: false);
                        LogEvent("RemoveUsernameFromRegistry - Username removed from registry.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent($"RemoveUsernameFromRegistry - Error: {ex.Message}", EventLogEntryType.Error);
                // Do not throw an exception to avoid interrupting the uninstallation
            }
        }

        // Method to enable Auto-Logon
        private void EnableAutoLogon(string username, string password)
        {
            LogEvent($"EnableAutoLogon - Configuring auto-logon for user: {username}");

            try
            {
                // Use RegistryView.Registry64 to avoid registry redirection on 64-bit systems
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey winlogonKey = baseKey.OpenSubKey(WINLOGON_REG_PATH, writable: true))
                    {
                        if (winlogonKey != null)
                        {
                            // Set the necessary values for auto-logon
                            winlogonKey.SetValue(AUTO_ADMIN_LOGON, "1", RegistryValueKind.String);
                            winlogonKey.SetValue(DEFAULT_USERNAME, username, RegistryValueKind.String);
                            winlogonKey.SetValue(DEFAULT_PASSWORD, password, RegistryValueKind.String);
                            winlogonKey.SetValue(DEFAULT_DOMAIN_NAME, Environment.MachineName, RegistryValueKind.String);
                            // Removed ForceAutoLogon to prevent auto-logon after logout

                            LogEvent("EnableAutoLogon - Auto-logon configured successfully.");
                        }
                        else
                        {
                            string errorMessage = $"Unable to open registry key: {WINLOGON_REG_PATH}";
                            LogEvent($"EnableAutoLogon - Error: {errorMessage}", EventLogEntryType.Error);
                            throw new Exception(errorMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent($"EnableAutoLogon - Error: {ex.Message}", EventLogEntryType.Error);
                throw new InstallException("Failed to configure auto-logon: " + ex.Message, ex);
            }
        }

        // Method to disable Auto-Logon
        private void DisableAutoLogon()
        {
            LogEvent("DisableAutoLogon - Disabling auto-logon.");

            try
            {
                // Use RegistryView.Registry64 to avoid registry redirection on 64-bit systems
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey winlogonKey = baseKey.OpenSubKey(WINLOGON_REG_PATH, writable: true))
                    {
                        if (winlogonKey != null)
                        {
                            // Reset auto-logon values
                            winlogonKey.SetValue(AUTO_ADMIN_LOGON, "0", RegistryValueKind.String);
                            winlogonKey.DeleteValue(DEFAULT_USERNAME, throwOnMissingValue: false);
                            winlogonKey.DeleteValue(DEFAULT_PASSWORD, throwOnMissingValue: false);
                            winlogonKey.DeleteValue(DEFAULT_DOMAIN_NAME, throwOnMissingValue: false);
                            // Removed ForceAutoLogon as it is no longer used

                            LogEvent("DisableAutoLogon - Auto-logon disabled successfully.");
                        }
                        else
                        {
                            string errorMessage = $"Unable to open registry key: {WINLOGON_REG_PATH}";
                            LogEvent($"DisableAutoLogon - Error: {errorMessage}", EventLogEntryType.Error);
                            throw new Exception(errorMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent($"DisableAutoLogon - Error: {ex.Message}", EventLogEntryType.Error);
                // Do not throw an exception to avoid interrupting the uninstallation
            }
        }

        // Method to log events to the Windows Event Log
        private void LogEvent(string message, EventLogEntryType entryType = EventLogEntryType.Information)
        {
            string source = "WinKioskInstaller";

            try
            {
                // Check if the event source exists, if not, create it
                if (!EventLog.SourceExists(source))
                {
                    EventLog.CreateEventSource(source, "Application");
                }

                EventLog.WriteEntry(source, message, entryType);
            }
            catch (Exception)
            {
                // Ignore logging errors to prevent infinite loops
            }
        }
    }
}
