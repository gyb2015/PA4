using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace WebRole1
{
    public class WebRole : RoleEntryPoint
    {
        public const string AZURE_COMMAND_QUEUE = "cmdqueue";
        public const string AZURE_SITEMAP_QUEUE = "smqueue";
        public const string AZURE_CRAWLING_QUEUE = "cwlrqueue";
        public const string AZURE_TITLE_QUEUE = "titlequeue";
        public const string AZURE_CRAWLING_TABLE = "cwlrtable";

        public override bool OnStart()
        {
            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            return base.OnStart();
        }
    }
}
