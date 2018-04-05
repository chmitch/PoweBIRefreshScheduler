using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.PowerBI.Api.V2;
using Microsoft.PowerBI.Api.V2.Models;
using System.Configuration;
using Microsoft.Rest;

namespace PowerBIRefreshScheduler
{
    public static class PowerBIRefreshScheduler
    {
        [FunctionName("PowerBIRefreshScheduler")]
        public static void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");

            //Get the hour and minute with leading zeros.  Not really necessary but it makes string sorting nicer in storage explorer.
            //Note:  DateTime.Now.Hour always returns the hour in a 24 hour clock so rows in table storage should be 00 - 23
            string hour = DateTime.Now.Hour.ToString("D2");
            string minute = DateTime.Now.Minute.ToString("D2");
                        
            //Get the list of datasets scheduled for the current our and 5 minute window.
            List <ScheduleItem> datasets = GetDatasets(hour, minute, log);

            if (datasets.Count != 0)
            {
                //Kickoff the refresh action for each defined dataset.
                RefreshModels(datasets, log);
            }
            else
            {
                log.Info($"No datasets to refresh for hour: {hour} and minute: {minute}");
            }
        }

        /// <summary>
        /// This method connects to table storage and queries for all the workspace/dataset combinations that are scheduled to
        /// refresh at the current timeslot.  This can run in one of two ways depending on the config setting of ignoreHour.
        /// 
        /// If ignoreHour = True then the scehduler assumes that every entry defines the minute you want the refresh to kickoff 
        /// and will do this every hour, and will consequently not filter the Azure Table on hour only on minute.  If you need 
        /// multiple refreshes in a given hour you will need one row for each timeslot you want to refresh within the hour.  
        /// Therefore if you want a dataset to refresh twice an hour you need two entries and ignoreHour set to true.
        /// 
        /// If ignoreHoure = False then the scheduler will filter the AzureTable for both hour and minute.  This enables scenarios
        /// where you need refresh schedules that don't align to hour boundaries like every 90 minutes, every 2 hours, etc.  For 
        /// this approach, you will need one row in the table for each time you want the refresh to run.
        /// </summary>
        /// <param name="hour">The current hour to query for in 00 format.</param>
        /// <param name="minute">The current minute to query for in 00 format.</param>
        /// <param name="log">The logwriter for telemetry</param>
        /// <returns>A list of ScheduleItems retreived form table storage.</returns>
        private static List<ScheduleItem> GetDatasets(string hour, string minute, TraceWriter log)
        {
            List<ScheduleItem> datasets = new List<ScheduleItem>();

            // Retrieve storage account information from connection string.
            string storageConnectionString = ConfigurationManager.AppSettings["TableConnectionString"];
            bool ignoreHour = ConfigurationManager.AppSettings["ignoreHour"] == "True" ? true : false;

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            // Create a table client for interacting with the table service
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference("RefreshSchedule");
           
            try
            {
                //Get all the scheduled dataset refreshes for the current minute and hour combination.
                IEnumerable<ScheduleItem> query;
                if (ignoreHour)
                {
                    //For scenarios where you want to run at the same time every hour.
                    query = from scheduleItem in table.CreateQuery<ScheduleItem>()
                            where scheduleItem.minute == minute
                            select scheduleItem;
                }
                else
                {
                    //For scenarios where the schedules are not hourly (ie. every 90 minutes, every two hours etc).
                    query = from scheduleItem in table.CreateQuery<ScheduleItem>()
                            where scheduleItem.minute == minute &&
                                  scheduleItem.hour == hour
                            select scheduleItem;
                }

                //Build result list for the query.
                foreach (ScheduleItem scheduleItem in query)
                {
                    datasets.Add(scheduleItem);
                }

                //log progress
                log.Info($"Retreived {datasets.Count} records to process from RequestSchedule using hour:{hour} and minute:{minute}");

            }
            catch (Exception e) {
                log.Warning($"Failed quering azure table. {e.InnerException.ToString()}");
            }

            return datasets;
        }

        /// <summary>
        /// This method takes a retreived list of ScheduleItems and kicks off the dataset refresh usign the Power BI api.
        /// 
        /// Note:  Due to the way refreshes work in Power BI, I have not yet implemented any logic to track the completion of a refresh.
        /// </summary>
        /// <param name="datasets">The list of scheudle items to refresh.</param>
        /// <param name="log">The logwriter for telemetry purposes.</param>
        private static async void RefreshModels(List<ScheduleItem> datasets, TraceWriter log)
        {
            //Get necessary settings from the config file.
            string username = ConfigurationManager.AppSettings["pbiUsername"];
            string password = ConfigurationManager.AppSettings["pbiPassword"];
            string clientId = ConfigurationManager.AppSettings["clientId"];

            //Do these things ever change?!
            string authorityUrl = ConfigurationManager.AppSettings["authorityUrl"];
            string resourceUrl = ConfigurationManager.AppSettings["resourceUrl"];
            string apiUrl = ConfigurationManager.AppSettings["apiUrl"];

            var credential = new UserPasswordCredential(username, password);

            // Authenticate using credentials retreived from the config file.
            var authenticationContext = new AuthenticationContext(authorityUrl);
            var authenticationResult = await authenticationContext.AcquireTokenAsync(resourceUrl, clientId, credential);

            //Check to make sure we have a valid auth token.
            if (authenticationResult == null)
            {
                log.Warning($"Failed aquiring Azure AD Authentiation token to resource: {resourceUrl} for clientId {clientId} with username:  {username}");
            }

            //Build a bearer token using the AAD access token for the Power BI Api call.
            var tokenCredentials = new TokenCredentials(authenticationResult.AccessToken, "Bearer");

            //Create the Power BI Client with the bearer token
            using (var client = new PowerBIClient(new Uri(apiUrl), tokenCredentials))
            {
                foreach (ScheduleItem dataset in datasets)
                {
                    log.Info($"Starting refresh for dataset {dataset.dataset} in workspace {dataset.workspace}");

                    //Execute the refresh for the workspace and dataset combination.
                    await client.Datasets.RefreshDatasetInGroupAsync(dataset.workspace, dataset.dataset);

                    /*
                    //Leaving this commented out for now.  I'm worried about long running refreshes causing the function to timeout.
                    //Considering a switch to durable functions.
                    
                    string status = "Unknown";

                    //Valid statuses are
                    //Unknown -- this is a refresh without an endTime aka in progress.
                    //Completed - self explanitory
                    //Failed -- self explanitory
                    //Disabled -- we shouldn't see this because we kicked it off manually.
                    while (status == "Unknown")
                    {
                        //Get the most recent record from the refresh history which should be the refresh we just kicked off
                        ODataResponseListRefresh hist = await client.Datasets.GetRefreshHistoryInGroupAsync(dataset.workspace, dataset.dataset, 1);

                        //unpack the status from the odata response.
                        status = hist.Value[0].Status;
                    */
                }
            }
        }
    }
}
