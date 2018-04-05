# PowerBIRefreshScheduler

NOTE:  this assumes you've already done the necessary AAD app registration to connect to and use the Power BI Api.  If you haven't read this documentation to prepare your environment
https://powerbi.microsoft.com/en-us/documentation/powerbi-developer-using-embed-token/

To publish and run this funciton there are a few steps 
1. Create the function app in Azure.
2. Provision a new Table called "RefreshSchedule" in the storage account, and populate it with some records to drive refresh.
3. Update the local.settings.json file if you'd like to test locally with the configuration settings defined below.
4. Using visual studio build a publish profile and deploy.
5. The publish profile doesn't automatically copy over the necessary app settings so you'll either need to manually copy the values in the local.settins.json file into the function app's app settings, or create them via powershell the first time.  Subesquent deployments will leave the settings untouched so this is a onetime action.

The configuration values this solution depends on are (not the first three are likely static for all deployments)

authorityUrl - https://login.windows.net/common/oauth2/authorize/ - this is the AAD oauth2 endpoint.

resourceUrl - https://analysis.windows.net/powerbi/api - this is the Application reource we're going to authenticate access to via aad. 

apiUrl - https://api.powerbi.com/ - this is the power bi API endpoint.

TableConnectionString - The code will need to connect to table storage to read configuration information regarding which power bi datasets to refresh.

ignoreHour - This helps drive some of the application functionality which is described later.

clientId - Id of AAD application registered as native app.

pbiUsername - Power BI username (e.g. Email). Must be an admin of the workspaces where the datasets live.

pbiPassword - password of Power BI user above.

### Important
For security reasons, in real application, don't save the user and password in web.config. Consider using KeyVault
TODO - add keyvalut capabilities to the function.
