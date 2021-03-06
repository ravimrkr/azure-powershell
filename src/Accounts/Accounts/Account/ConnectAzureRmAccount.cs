﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Management.Automation;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.Commands.Common.Authentication;
using Microsoft.Azure.Commands.Common.Authentication.Abstractions;
using Microsoft.Azure.Commands.Common.Authentication.Factories;
using Microsoft.Azure.Commands.Common.Authentication.Models;
using Microsoft.Azure.Commands.Profile.Common;
using Microsoft.Azure.Commands.Profile.Models.Core;
using Microsoft.Azure.Commands.Profile.Properties;
using Microsoft.Azure.Commands.ResourceManager.Common;
using Microsoft.Azure.PowerShell.Authenticators;
using Microsoft.WindowsAzure.Commands.Common;
using Microsoft.WindowsAzure.Commands.Utilities.Common;

namespace Microsoft.Azure.Commands.Profile
{
    /// <summary>
    /// Cmdlet to log into an environment and download the subscriptions
    /// </summary>
    [Cmdlet("Connect", AzureRMConstants.AzureRMPrefix + "Account", DefaultParameterSetName = "UserWithSubscriptionId", SupportsShouldProcess=true)]
    [Alias("Login-AzAccount", "Login-AzureRmAccount", "Add-" + AzureRMConstants.AzureRMPrefix + "Account")]
    [OutputType(typeof(PSAzureProfile))]
    public class ConnectAzureRmAccountCommand : AzureContextModificationCmdlet, IModuleAssemblyInitializer
    {
        public const string UserParameterSet = "UserWithSubscriptionId";
        public const string UserWithCredentialParameterSet = "UserWithCredential";
        public const string ServicePrincipalParameterSet = "ServicePrincipalWithSubscriptionId";
        public const string ServicePrincipalCertificateParameterSet= "ServicePrincipalCertificateWithSubscriptionId";
        public const string AccessTokenParameterSet = "AccessTokenWithSubscriptionId";
        public const string ManagedServiceParameterSet = "ManagedServiceLogin";
        public const string MSIEndpointVariable = "MSI_ENDPOINT";
        public const string MSISecretVariable = "MSI_SECRET";
        public const int DefaultMaxContextPopulation = 25;
        public const string DefaultMaxContextPopulationString = "25";

        private IAzureEnvironment _environment;

        [Parameter(Mandatory = false, HelpMessage = "Name of the environment containing the account to log into")]
        [Alias("EnvironmentName")]
        [ValidateNotNullOrEmpty]
        public string Environment { get; set; }

        [Parameter(ParameterSetName = ServicePrincipalParameterSet,
                    Mandatory = true, HelpMessage = "Service Principal Secret")]
        [Parameter(ParameterSetName = UserWithCredentialParameterSet,
                    Mandatory = true, HelpMessage = "Username/Password Credential")]
        public PSCredential Credential { get; set; }

        [Parameter(ParameterSetName = ServicePrincipalCertificateParameterSet,
                    Mandatory = true, HelpMessage = "Certificate Hash (Thumbprint)")]
        public string CertificateThumbprint { get; set; }

        [Parameter(ParameterSetName = ServicePrincipalCertificateParameterSet,
                    Mandatory = true, HelpMessage = "SPN")]
        public string ApplicationId { get; set; }

        [Parameter(ParameterSetName = ServicePrincipalParameterSet,
                    Mandatory = true)]
        [Parameter(ParameterSetName = ServicePrincipalCertificateParameterSet,
                    Mandatory = false)]
        public SwitchParameter ServicePrincipal { get; set; }

        [Parameter(ParameterSetName = UserParameterSet,
                    Mandatory = false, HelpMessage = "Optional tenant name or ID")]
        [Parameter(ParameterSetName = UserWithCredentialParameterSet,
                    Mandatory = false, HelpMessage = "Optional tenant name or ID")]
        [Parameter(ParameterSetName = ServicePrincipalParameterSet,
                    Mandatory = true, HelpMessage = "Tenant name or ID")]
        [Parameter(ParameterSetName = AccessTokenParameterSet,
                    Mandatory = false, HelpMessage = "Tenant name or ID")]
        [Parameter(ParameterSetName = ServicePrincipalCertificateParameterSet,
                    Mandatory = true, HelpMessage = "Tenant name or ID")]
        [Parameter(ParameterSetName = ManagedServiceParameterSet,
                    Mandatory = false, HelpMessage = "Optional tenant name or ID")]
        [Alias("Domain", "TenantId")]
        [ValidateNotNullOrEmpty]
        public string Tenant { get; set; }

        [Parameter(ParameterSetName = AccessTokenParameterSet,
                    Mandatory = true, HelpMessage = "AccessToken for Azure Resource Manager")]
        [ValidateNotNullOrEmpty]
        public string AccessToken { get; set; }

        [Parameter(ParameterSetName = AccessTokenParameterSet,
                   Mandatory = false, HelpMessage = "AccessToken for Graph Service")]
        [ValidateNotNullOrEmpty]
        public string GraphAccessToken { get; set; }

        [Parameter(ParameterSetName = AccessTokenParameterSet,
                   Mandatory = false, HelpMessage = "AccessToken for KeyVault Service")]
        [ValidateNotNullOrEmpty]
        public string KeyVaultAccessToken { get; set; }

        [Parameter(ParameterSetName = AccessTokenParameterSet,
                    Mandatory = true, HelpMessage = "Account Id for access token")]
        [Parameter(ParameterSetName = ManagedServiceParameterSet,
                    Mandatory = false, HelpMessage = "Account Id for managed service. Can be a managed service resource Id, or the associated client id. To use the SystemAssigned identity, leave this field blank.")]
        [ValidateNotNullOrEmpty]
        public string AccountId { get; set; }

        [Parameter(ParameterSetName = ManagedServiceParameterSet, Mandatory =true, HelpMessage = "Login using managed service identity in the current environment.")]
        [Alias("MSI", "ManagedService")]
        public SwitchParameter Identity { get; set; }

        [Parameter(ParameterSetName = ManagedServiceParameterSet, Mandatory = false, HelpMessage = "Port number for managed service login.")]
        [PSDefaultValue(Help = "50342", Value = 50342)]
        public int ManagedServicePort { get; set; } = 50342;

        [Parameter(ParameterSetName = ManagedServiceParameterSet, Mandatory = false, HelpMessage = "Host name for managed service login.")]
        [PSDefaultValue(Help = "localhost", Value = "localhost")]
        public string ManagedServiceHostName { get; set; } = "localhost";

        [Parameter(ParameterSetName = ManagedServiceParameterSet, Mandatory = false, HelpMessage = "Secret, used for some kinds of managed service login.")]
        [ValidateNotNullOrEmpty]
        public SecureString ManagedServiceSecret { get; set; }


        [Alias("SubscriptionName", "SubscriptionId")]
        [Parameter(ParameterSetName = UserParameterSet,
                    Mandatory = false, HelpMessage = "Subscription Name or ID", ValueFromPipeline = true)]
        [Parameter(ParameterSetName = UserWithCredentialParameterSet,
                    Mandatory = false, HelpMessage = "Subscription Name or ID", ValueFromPipeline = true)]
        [Parameter(ParameterSetName = ServicePrincipalParameterSet,
                    Mandatory = false, HelpMessage = "Subscription Name or ID", ValueFromPipeline = true)]
        [Parameter(ParameterSetName = ServicePrincipalCertificateParameterSet,
                    Mandatory = false, HelpMessage = "Subscription Name or ID", ValueFromPipeline = true)]
        [Parameter(ParameterSetName = AccessTokenParameterSet,
                    Mandatory = false, HelpMessage = "Subscription Name or ID", ValueFromPipeline = true)]
        [Parameter(ParameterSetName = ManagedServiceParameterSet,
                    Mandatory = false, HelpMessage = "Subscription Name or ID", ValueFromPipeline = true)]
        [ValidateNotNullOrEmpty]
        public string Subscription { get; set; }

        [Parameter(Mandatory = false, HelpMessage = "Name of the default context from this login")]
        [ValidateNotNullOrEmpty]
        public string ContextName { get; set; }

        [Parameter(ParameterSetName = AccessTokenParameterSet,
                    Mandatory = false, HelpMessage = "Skip validation for access token")]
        public SwitchParameter SkipValidation { get; set; }

        [Parameter(Mandatory = false, HelpMessage = "Skips context population if no contexts are found.")]
        public SwitchParameter SkipContextPopulation { get; set; }

        [Parameter(ParameterSetName = UserParameterSet, Mandatory = false, HelpMessage = "Max subscription number to populate contexts after login. Default is "+ DefaultMaxContextPopulationString + ". To populate all subscriptions to contexts, set to -1.")]
        [Parameter(ParameterSetName = UserWithCredentialParameterSet, Mandatory = false, HelpMessage = "Max subscription number to populate contexts after login. Default is " + DefaultMaxContextPopulationString + ". To populate all subscriptions to contexts, set to -1.")]
        [Parameter(ParameterSetName = ServicePrincipalParameterSet, Mandatory = false, HelpMessage = "Max subscription number to populate contexts after login. Default is " + DefaultMaxContextPopulationString + ". To populate all subscriptions to contexts, set to -1.")]
        [Parameter(ParameterSetName = ServicePrincipalCertificateParameterSet, Mandatory = false, HelpMessage = "Max subscription number to populate contexts after login. Default is " + DefaultMaxContextPopulationString + ". To populate all subscriptions to contexts, set to -1.")]
        [Parameter(ParameterSetName = AccessTokenParameterSet, Mandatory = false, HelpMessage = "Max subscription number to populate contexts after login. Default is " + DefaultMaxContextPopulationString + ". To populate all subscriptions to contexts, set to -1.")]
        [Parameter(ParameterSetName = ManagedServiceParameterSet, Mandatory = false, HelpMessage = "Max subscription number to populate contexts after login. Default is " + DefaultMaxContextPopulationString + ". To populate all subscriptions to contexts, set to -1.")]
        [PSDefaultValue(Help = DefaultMaxContextPopulationString, Value = DefaultMaxContextPopulation)]
        [ValidateRange(-1,int.MaxValue)]
        public int MaxContextPopulation { get; set; } = DefaultMaxContextPopulation;

        [Parameter(ParameterSetName = UserParameterSet,
                   Mandatory = false, HelpMessage = "Use device code authentication instead of a browser control")]
        [Alias("DeviceCode", "DeviceAuth", "Device")]
        public SwitchParameter UseDeviceAuthentication { get; set; }

        [Parameter(Mandatory = false, HelpMessage = "Overwrite the existing context with the same name, if any.")]
        public SwitchParameter Force { get; set; }

        protected override IAzureContext DefaultContext
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// This cmdlet should work even if there isn't a default context
        /// </summary>
        protected override bool RequireDefaultContext() { return false; }

        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            if (AzureEnvironment.PublicEnvironments.ContainsKey(EnvironmentName.AzureCloud))
            {
                _environment = AzureEnvironment.PublicEnvironments[EnvironmentName.AzureCloud];
            }
            else
            {
                WriteWarning($"Default environment {EnvironmentName.AzureCloud} cannot be found from PublicEnvironment list. ");
                WriteWarning("You can get current list via [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureEnvironment]::PublicEnvironments");
            }
            if (MyInvocation.BoundParameters.ContainsKey(nameof(Environment)))
            {
                var profile = GetDefaultProfile();
                if (!profile.TryGetEnvironment(Environment, out _environment))
                {
                    throw new PSInvalidOperationException(
                        string.Format(Resources.UnknownEnvironment, Environment));
                }
            }

            // save the target environment so it can be read to get the correct accounts from token cache
            AzureSession.Instance.SetProperty(AzureSession.Property.Environment, Environment);

            _writeWarningEvent -= WriteWarningSender;
            _writeWarningEvent += WriteWarningSender;
            // store the original write warning handler, register a thread safe one
            AzureSession.Instance.TryGetComponent(WriteWarningKey, out _originalWriteWarning);
            AzureSession.Instance.UnregisterComponent<EventHandler<StreamEventArgs>>(WriteWarningKey);
            AzureSession.Instance.RegisterComponent(WriteWarningKey, () => _writeWarningEvent);
        }

        private event EventHandler<StreamEventArgs> _writeWarningEvent;
        private event EventHandler<StreamEventArgs> _originalWriteWarning;

        private void WriteWarningSender(object sender, StreamEventArgs args)
        {
            _tasks.Enqueue(new Task(() => this.WriteWarning(args.Message)));
        }

        public override void ExecuteCmdlet()
        {
            Guid subscrptionIdGuid;
            string subscriptionName = null;
            string subscriptionId = null;
            if (MyInvocation.BoundParameters.ContainsKey(nameof(Subscription)))
            {
                if (Guid.TryParse(Subscription, out subscrptionIdGuid))
                {
                    subscriptionId = Subscription;
                }
                else
                {
                    subscriptionName = Subscription;
                }

            }

            var azureAccount = new AzureAccount();

            switch (ParameterSetName)
            {
                case AccessTokenParameterSet:
                    azureAccount.Type = AzureAccount.AccountType.AccessToken;
                    azureAccount.Id = AccountId;
                    azureAccount.SetProperty(AzureAccount.Property.AccessToken, AccessToken);
                    azureAccount.SetProperty(AzureAccount.Property.GraphAccessToken, GraphAccessToken);
                    azureAccount.SetProperty(AzureAccount.Property.KeyVaultAccessToken, KeyVaultAccessToken);
                    break;
                case ServicePrincipalCertificateParameterSet:
                case ServicePrincipalParameterSet:
                    azureAccount.Type = AzureAccount.AccountType.ServicePrincipal;
                    break;
                case ManagedServiceParameterSet:
                    azureAccount.Type = AzureAccount.AccountType.ManagedService;
                    var builder = new UriBuilder
                    {
                        Scheme = "http",
                        Host = ManagedServiceHostName,
                        Port = ManagedServicePort,
                        Path = "/oauth2/token"
                    };

                    var envSecret = System.Environment.GetEnvironmentVariable(MSISecretVariable);

                    var msiSecret = this.IsBound(nameof(ManagedServiceSecret))
                        ? ManagedServiceSecret.ConvertToString()
                        : envSecret;

                    var envUri = System.Environment.GetEnvironmentVariable(MSIEndpointVariable);

                    var suppliedUri = this.IsBound(nameof(ManagedServiceHostName))
                        ? builder.Uri.ToString()
                        : envUri;

                    if (!this.IsBound(nameof(ManagedServiceHostName)) && !string.IsNullOrWhiteSpace(envUri) 
                        && !this.IsBound(nameof(ManagedServiceSecret)) && !string.IsNullOrWhiteSpace(envSecret))
                    {
                        // set flag indicating this is AppService Managed Identity ad hoc mode
                        azureAccount.SetProperty(AuthenticationFactory.AppServiceManagedIdentityFlag, "the value not used");
                    }

                    if (!string.IsNullOrWhiteSpace(msiSecret))
                    {
                        azureAccount.SetProperty(AzureAccount.Property.MSILoginSecret, msiSecret);
                    }

                    if (!string.IsNullOrWhiteSpace(suppliedUri))
                    {
                        azureAccount.SetProperty(AzureAccount.Property.MSILoginUri, suppliedUri);
                    }
                    else
                    {
                        azureAccount.SetProperty(AzureAccount.Property.MSILoginUriBackup, builder.Uri.ToString());
                        azureAccount.SetProperty(AzureAccount.Property.MSILoginUri, AuthenticationFactory.DefaultMSILoginUri);
                    }

                    azureAccount.Id = this.IsBound(nameof(AccountId)) ? AccountId : string.Format("MSI@{0}", ManagedServicePort);
                    break;
                default:
                    //Support username + password for both Windows PowerShell and PowerShell 6+
                    azureAccount.Type = AzureAccount.AccountType.User;
                    break;
            }

            SecureString password = null;
            if (Credential != null)
            {
                azureAccount.Id = Credential.UserName;
                password = Credential.Password;
            }

            if (UseDeviceAuthentication.IsPresent)
            {
                azureAccount.SetProperty("UseDeviceAuth", "true");
            }

            if(azureAccount.Type == AzureAccount.AccountType.User && password != null)
            {
                azureAccount.SetProperty(AzureAccount.Property.UsePasswordAuth, "true");
            }

            if (!string.IsNullOrEmpty(ApplicationId))
            {
                azureAccount.Id = ApplicationId;
            }

            if (!string.IsNullOrWhiteSpace(CertificateThumbprint))
            {
                azureAccount.SetThumbprint(CertificateThumbprint);
            }

            if (!string.IsNullOrEmpty(Tenant))
            {
                azureAccount.SetProperty(AzureAccount.Property.Tenants, Tenant);
            }

            if (azureAccount.Type == AzureAccount.AccountType.ServicePrincipal && string.IsNullOrEmpty(CertificateThumbprint))
            {
                azureAccount.SetProperty(AzureAccount.Property.ServicePrincipalSecret, password.ConvertToString());
                if (GetContextModificationScope() == ContextModificationScope.CurrentUser)
                {
                    var file = AzureSession.Instance.ARMProfileFile;
                    var directory = AzureSession.Instance.ARMProfileDirectory;
                    WriteWarning(string.Format(Resources.ServicePrincipalWarning, file, directory));
                }
            }

            if (ShouldProcess(string.Format(Resources.LoginTarget, azureAccount.Type, _environment.Name), "log in"))
            {
                if (AzureRmProfileProvider.Instance.Profile == null)
                {
                    InitializeProfileProvider();
                }

                SetContextWithOverwritePrompt((localProfile, profileClient, name) =>
               {
                   bool shouldPopulateContextList = true;
                   if (this.IsParameterBound(c => c.SkipContextPopulation))
                   {
                       shouldPopulateContextList = false;
                   }

                   profileClient.WarningLog = (message) => _tasks.Enqueue(new Task(() => this.WriteWarning(message)));
                   var task = new Task<AzureRmProfile>( () => profileClient.Login(
                        azureAccount,
                        _environment,
                        Tenant,
                        subscriptionId,
                        subscriptionName,
                        password,
                        SkipValidation,
                        WriteWarning,
                        name,
                        shouldPopulateContextList,
                        MaxContextPopulation));
                   task.Start();
                   while (!task.IsCompleted)
                   {
                       HandleActions();
                       Thread.Yield();
                   }

                   HandleActions();
                   var result = (PSAzureProfile) (task.ConfigureAwait(false).GetAwaiter().GetResult());
                   WriteObject(result);
               });
            }
        }

        private ConcurrentQueue<Task> _tasks = new ConcurrentQueue<Task>();

        private void HandleActions()
        {
            Task task;
            while (_tasks.TryDequeue(out task))
            {
                task.RunSynchronously();
            }
        }

        private void WriteWarningEvent(string message)
        {
            EventHandler<StreamEventArgs> writeWarningEvent;
            if (AzureSession.Instance.TryGetComponent(WriteWarningKey, out writeWarningEvent))
            {
                writeWarningEvent(this, new StreamEventArgs() { Message = message });
            }
        }

        private static bool CheckForExistingContext(AzureRmProfile profile, string name)
        {
            return name != null && profile?.Contexts != null && profile.Contexts.ContainsKey(name);
        }

        private void SetContextWithOverwritePrompt(Action<AzureRmProfile, RMProfileClient, string> setContextAction)
        {
            string name = null;
            if (MyInvocation.BoundParameters.ContainsKey(nameof(ContextName)))
            {
                name = ContextName;
            }

            AzureRmProfile profile = null;
            bool? originalShouldRefreshContextsFromCache = null;
            try
            {
                profile = DefaultProfile as AzureRmProfile;
                if (profile != null)
                {
                    originalShouldRefreshContextsFromCache = profile.ShouldRefreshContextsFromCache;
                    profile.ShouldRefreshContextsFromCache = false;
                }
            if (!CheckForExistingContext(profile, name)
                || Force.IsPresent
                || ShouldContinue(string.Format(Resources.ReplaceContextQuery, name),
                string.Format(Resources.ReplaceContextCaption, name)))
            {
                ModifyContext((prof, client) => setContextAction(prof, client, name));
            }
        }
            finally
            {
                if(profile != null && originalShouldRefreshContextsFromCache.HasValue)
                {
                    profile.ShouldRefreshContextsFromCache = originalShouldRefreshContextsFromCache.Value;
                }
            }
        }

        /// <summary>
        /// Load global aliases for ARM
        /// </summary>
        public void OnImport()
        {
#if DEBUG
            try
            {
#endif
                 AzureSessionInitializer.InitializeAzureSession();
#if DEBUG
                if (!TestMockSupport.RunningMocked)
                {
#endif
                    AzureSession.Instance.DataStore = new DiskDataStore();
#if DEBUG
                }
#endif

                var autoSaveEnabled = AzureSession.Instance.ARMContextSaveMode == ContextSaveMode.CurrentUser;
                var autosaveVariable = System.Environment.GetEnvironmentVariable(AzureProfileConstants.AzureAutosaveVariable);

                if(bool.TryParse(autosaveVariable, out bool localAutosave))
                {
                    autoSaveEnabled = localAutosave;
                }

                if (autoSaveEnabled && !SharedTokenCacheProvider.SupportCachePersistence(out string message))
                {
                    // If token cache persistence is not supported, fall back to plain text persistence, and print a warning
                    // We cannot just throw an exception here because this is called when importing the module
                    WriteInitializationWarnings(Resources.TokenCacheEncryptionNotSupportedWithFallback);
                }

                if(!InitializeProfileProvider(autoSaveEnabled))
                {
                    AzureSession.Instance.ARMContextSaveMode = ContextSaveMode.Process;
                    autoSaveEnabled = false;
                }

                IServicePrincipalKeyStore keyStore =
                    new AzureRmServicePrincipalKeyStore(AzureRmProfileProvider.Instance.Profile);
                AzureSession.Instance.RegisterComponent(ServicePrincipalKeyStore.Name, () => keyStore);

                IAuthenticatorBuilder builder = null;
                if (!AzureSession.Instance.TryGetComponent(AuthenticatorBuilder.AuthenticatorBuilderKey, out builder))
                {
                    builder = new DefaultAuthenticatorBuilder();
                    AzureSession.Instance.RegisterComponent(AuthenticatorBuilder.AuthenticatorBuilderKey, () => builder);
                }

                PowerShellTokenCacheProvider provider = null;
                if (autoSaveEnabled)
                {
                    provider = new SharedTokenCacheProvider();
                }
                else // if autosave is disabled, or the shared factory fails to initialize, we fallback to in memory
                {
                    provider = new InMemoryTokenCacheProvider();
                }
                var tokenCache = provider.GetTokenCache();
                IAzureEventListenerFactory azureEventListenerFactory = new AzureEventListenerFactory();
                AzureSession.Instance.RegisterComponent(PowerShellTokenCacheProvider.PowerShellTokenCacheProviderKey, () => provider);
                AzureSession.Instance.RegisterComponent(nameof(IAzureEventListenerFactory), () => azureEventListenerFactory);
                AzureSession.Instance.RegisterComponent(nameof(PowerShellTokenCache), () => tokenCache);

#if DEBUG
            }
            catch (Exception) when (TestMockSupport.RunningMocked)
            {
                // This will throw exception for tests, ignore.
            }
#endif
        }

        protected override void EndProcessing()
        {
            base.EndProcessing();
            // unregister the thread-safe write warning, because it won't work out of this cmdlet
            AzureSession.Instance.UnregisterComponent<EventHandler<StreamEventArgs>>(WriteWarningKey);
            AzureSession.Instance.RegisterComponent(WriteWarningKey, () => _originalWriteWarning);
        }
    }
}
