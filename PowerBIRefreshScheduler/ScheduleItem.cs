using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//using Microsoft.Azure.CosmosDB.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace PowerBIRefreshScheduler
{
    class ScheduleItem: TableEntity
    {
        public string hour { get; set; }

        public string minute { get; set; }

        public string workspace { get; set; }

        public string dataset { get;set; }
    }
}