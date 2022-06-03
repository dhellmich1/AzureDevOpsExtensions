using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CloneWorkItem
{
    class Program
    {
        static string[] systemFields = { "System.IterationId", "System.ExternalLinkCount", "System.HyperLinkCount", "System.AttachedFileCount", "System.NodeName",
        "System.RevisedDate", "System.ChangedDate", "System.Id", "System.AreaId", "System.AuthorizedAs", "System.State", "System.AuthorizedDate", "System.Watermark",
            "System.Rev", "System.ChangedBy", "System.Reason", "System.WorkItemType", "System.CreatedDate", "System.CreatedBy", "System.History", "System.RelatedLinkCount",
        "System.BoardColumn", "System.BoardColumnDone", "System.BoardLane", "System.CommentCount", "System.TeamProject"}; //system fields to skip

        static string[] customFields = { "Microsoft.VSTS.Common.ActivatedDate", "Microsoft.VSTS.Common.ActivatedBy", "Microsoft.VSTS.Common.ResolvedDate", 
            "Microsoft.VSTS.Common.ResolvedBy", "Microsoft.VSTS.Common.ResolvedReason", "Microsoft.VSTS.Common.ClosedDate", "Microsoft.VSTS.Common.ClosedBy",
        "Microsoft.VSTS.Common.StateChangeDate"}; //unneeded fields to skip

        const string ChildRefStr = "System.LinkTypes.Hierarchy-Forward"; //should be only one parent
        const string RelatedRefStr = "System.LinkTypes.Related";
        const string ParentRefStr = "System.LinkTypes.Hierarchy-Reverse";


        static void Main(string[] args)
        {
            string pat = "..."; //https://docs.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate
            string orgUrl = "https://dev.azure.com/...";
            string oldProjectName = "...";
            string newProjectName = "";
            int wiIdToClone = 23577;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;// Or SecurityProtocolType.Tls11 Or SecurityProtocolType.Tls;
            VssConnection connection = new VssConnection(new Uri(orgUrl), new VssBasicCredential(string.Empty, pat));
            var witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            //CloneWorkItem(witClient, wiIdToClone, newProjectName, true);

            //var wiql = new Wiql()
            //{
            //    // NOTE: Even if other columns are specified, only the ID & URL are available in the WorkItemReference
            //    Query = "Select [Id] " +
            //        "From WorkItems " +
            //        "Where [Work Item Type] = 'Data Entity' " +
            //        "And [System.TeamProject] = '" + oldProjectName + "' " +
            //        "And [System.State] <> 'Closed' " +
            //        "Order By [State] Asc, [Changed Date] Desc"
            //};
            //var result = witClient.QueryByWiqlAsync(wiql);
            
            // Copy Link on Query name shows URL and GUID
            // Query muss vom Typ Flat sein!!
            // https://dev.azure.com/<organization>/<project>/_queries/query/d413e82f-e874-4ac6-b814-36a7621d77c7
            var result = witClient.QueryByIdAsync(new Guid("d413e82f-e874-4ac6-b814-36a7621d77c7")).Result;
            var workItems = result.WorkItems;
            if (workItems.Any())
            {
                foreach (var workItem in workItems)
                {
                    CloneWorkItem(witClient, workItem.Id, newProjectName, true, "US - ");
                }
            }
        }

        private static void CloneWorkItem(WorkItemTrackingHttpClient witClient, int wiIdToClone, string NewTeamProject = "", bool CopyLink = false, string addTitlePrefix = "")
        {
            WorkItem wiToClone = (CopyLink) ? witClient.GetWorkItemAsync(wiIdToClone, expand: WorkItemExpand.Relations).Result
                : witClient.GetWorkItemAsync(wiIdToClone).Result;

            string teamProjectName = (NewTeamProject != "") ? NewTeamProject : wiToClone.Fields["System.TeamProject"].ToString();
            string wiType = wiToClone.Fields["System.WorkItemType"].ToString();

            JsonPatchDocument patchDocument = new JsonPatchDocument();

            foreach (var key in wiToClone.Fields.Keys) //copy fields
                if (!systemFields.Contains(key) && !customFields.Contains(key))
                {
                    if (NewTeamProject == "" ||
                        (NewTeamProject != "" && key != "System.AreaPath" && key != "System.IterationPath")) //do not copy area and iteration into another project
                    {
                        var newValue = wiToClone.Fields[key];
                        if (addTitlePrefix != "" && key == "System.Title")
                        {
                            newValue = addTitlePrefix + newValue;
                        }
                        patchDocument.Add(new JsonPatchOperation()
                        {
                            Operation = Operation.Add,
                            Path = "/fields/" + key,
                            Value = newValue
                        });
                    }
                }

            if (CopyLink && wiToClone.Relations != null) //copy links
                foreach (var link in wiToClone.Relations)
                {
                    // Implementation Model PBI's are related
                    // ADF also stores hyperlinks 
                    //if (link.Rel != ChildRefStr && link.Rel != RelatedRefStr)
                    if (link.Rel == ParentRefStr)
                    {
                        patchDocument.Add(new JsonPatchOperation()
                        {
                            Operation = Operation.Add,
                            Path = "/relations/-",
                            Value = new
                            {
                                rel = link.Rel,
                                url = link.Url
                            }
                        });
                    }
                }
            Console.WriteLine($"{wiToClone.Fields["System.WorkItemType"]}: {wiToClone.Id} - {wiToClone.Fields["System.Title"]}");
            WorkItem clonedWi = witClient.CreateWorkItemAsync(patchDocument, teamProjectName, wiType).Result;

            Console.WriteLine("New work item: " + clonedWi.Id);
        }
    }
}
