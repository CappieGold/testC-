public override void Install(IDictionary stateSaver)
{
    base.Install(stateSaver);

    try
    {
        // Afficher la boîte de dialogue pour choisir le script
        string scriptPath = null;
        using (ScriptPathForm form = new ScriptPathForm())
        {
            if (form.ShowDialog() == DialogResult.OK)
            {
                scriptPath = form.ScriptPath;
            }
            else
            {
                throw new InstallException("L'utilisateur a annulé la sélection du script.");
            }
        }

        // Log retrieved values
        LogEvent($"Install - scriptPath: '{scriptPath}'");

        // Store the script path in the registry for later use by the application
        StoreScriptPathInRegistry(scriptPath);

        // Retrieve other parameters from CustomActionData
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


private void StoreScriptPathInRegistry(string scriptPath)
{
    try
    {
        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\WinKiosk"))
        {
            if (key != null)
            {
                key.SetValue("ScriptPath", scriptPath, RegistryValueKind.String);
                LogEvent($"StoreScriptPathInRegistry - ScriptPath '{scriptPath}' stored in registry.");
            }
            else
            {
                LogEvent("StoreScriptPathInRegistry - Unable to create or open registry key SOFTWARE\\WinKiosk.", EventLogEntryType.Error);
            }
        }
    }
    catch (Exception ex)
    {
        LogEvent($"StoreScriptPathInRegistry - Error: {ex.Message}", EventLogEntryType.Error);
        // Do not throw an exception to avoid interrupting the installation
    }
}
