using TSystems.RELOAD;

namespace ReloadMDI.Properties {
    
    
    // This class allows you to handle specific events on the settings class:
    //  The SettingChanging event is raised before a setting's value is changed.
    //  The PropertyChanged event is raised after a setting's value is changed.
    //  The SettingsLoaded event is raised after the setting values are loaded.
    //  The SettingsSaving event is raised before the setting values are saved.
    internal sealed partial class Settings {
        
        public Settings() {
            // // To add event handlers for saving and changing settings, uncomment the lines below:
            //
            // this.SettingChanging += this.SettingChangingEventHandler;
            //
            this.SettingsSaving += this.SettingsSavingEventHandler;
            //
        }
        
        private void SettingChangingEventHandler(object sender, System.Configuration.SettingChangingEventArgs e) {
            // Add code to handle the SettingChangingEvent event here.
        }
        
        private void SettingsSavingEventHandler(object sender, System.ComponentModel.CancelEventArgs e) {

            ReloadGlobals.Client = ReloadMDI.Properties.Settings.Default.Client;
            ReloadGlobals.TLS = ReloadMDI.Properties.Settings.Default.TLS;
            ReloadGlobals.TLS_PASSTHROUGH = ReloadMDI.Properties.Settings.Default.TLS_Passtrough;
            ReloadGlobals.IgnoreSSLErrors = ReloadMDI.Properties.Settings.Default.IgnoreSSLErrors;
            ReloadGlobals.ReportEnabled = ReloadMDI.Properties.Settings.Default.ReportEnabled;
            ReloadGlobals.ReportIncludeConnections = ReloadMDI.Properties.Settings.Default.ReportIncludeConnections;
            ReloadGlobals.TRACELEVEL = (ReloadGlobals.TRACEFLAGS)ReloadMDI.Properties.Settings.Default.TraceLevel;
            ReloadGlobals.ForceLocalConfig = ReloadMDI.Properties.Settings.Default.ForceLocalConfig;

            //we can't support multiple certs per commandline in this MDI app, we need enrollment server here
            if (ReloadGlobals.TLS)
                ReloadGlobals.ForceLocalConfig = false;
            
            ReloadGlobals.DNS_Address = ReloadMDI.Properties.Settings.Default.DNS_Address;
        }
    }
}
