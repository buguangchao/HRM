﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OptimaJet.DWKit.Core;
using OptimaJet.DWKit.Core.DataProvider;
using OptimaJet.DWKit.Core.Model;
using OptimaJet.HRM;

namespace OptimaJet.DWKit.Application
{
    public static class ClientNotifiers
    {
     
        
        public static async Task NotifyClientsAboutInboxStatus (string userId)
        {
            var inboxModel = await MetadataToModelConverter.GetEntityModelByModelAsync("WorkflowInbox");
            var historyModel = await MetadataToModelConverter.GetEntityModelByModelAsync("WorkflowProcessTransitionHistory");
             var inboxCount = await inboxModel.GetCountAsync(Filter.And.Equal(userId.ToString(), "IdentityId"));
            var outboxProcessIds = (await historyModel.GetAsync(Filter.And.Equal(userId, "ExecutorIdentityId"))).Select(e => (Guid) (e as dynamic).ProcessId).Distinct();
            var outboxCount = outboxProcessIds.Count();
            
            await SendInboxOutboxCountNotification(userId, inboxCount, outboxCount);
        }

        public static async Task NotifyClientsAboutInboxStatus (List<Guid> userIds)
        {
            var userIdsStrings = userIds.Select(id => id.ToString()).ToList();
            await NotifyClientsAboutInboxStatus(userIdsStrings);
        }

        public static async Task NotifyClientsAboutInboxStatus(List<string> userIds)
        {
            var inboxModel = await MetadataToModelConverter.GetEntityModelByModelAsync("WorkflowInbox");
            var historyModel = await MetadataToModelConverter.GetEntityModelByModelAsync("WorkflowProcessTransitionHistory");
            var inboxes = await inboxModel.GetAsync(Filter.And.In(userIds, "IdentityId"));
            var outboxes = await historyModel.GetAsync(Filter.And.In(userIds, "ExecutorIdentityId"));

            foreach (var id in userIds)
            {
                var inboxCount = inboxes.Count(i => (i as dynamic).IdentityId == id);
                var outboxCount = outboxes.Where(i => (i as dynamic).ExecutorIdentityId == id).Select(i => (Guid) (i as dynamic).ProcessId).Distinct().Count();
                await SendInboxOutboxCountNotification(id, inboxCount, outboxCount);
            }
        }

        private static async Task SendInboxOutboxCountNotification(string userId, long inboxCount, long outboxCount)
        {
            await DWKitRuntime.SendStateChangeToUserAsync(userId, HRMNotifier.SideMenuPath, new Dictionary<string, object>
            {
                {"inbox", inboxCount},
                {"outbox", outboxCount}
            });
        }
        
        public static async Task DeleteWokflowAndNotifyClients(EntityModel model, List<ChangeOperation> changes)
        {
            var processIds = changes.Select(c => (Guid) c.Entity.GetId()).ToList();
            if (!processIds.Any())
                return;
            var  inboxModel = await MetadataToModelConverter.GetEntityModelByModelAsync("WorkflowInbox");
            var inboxes = await inboxModel.GetAsync(Filter.And.In(processIds, "ProcessId"));
            var usersToNotify = inboxes.Select(i => (string) (i as dynamic).IdentityId).ToList();
            var historyModel = await MetadataToModelConverter.GetEntityModelByModelAsync("WorkflowProcessTransitionHistory");
            var histories = await historyModel.GetAsync(Filter.And.In(processIds, "ProcessId"));
            usersToNotify.AddRange(histories.Select(h=>(string)((h as dynamic).ExecutorIdentityId)));
            usersToNotify = usersToNotify.Distinct().Where(s => s != null).ToList(); //ExecutorIdentityId can be null for timer transitions
            foreach (var processId in processIds)
            {
                if (await WorkflowInit.Runtime.IsProcessExistsAsync(processId))
                    await WorkflowInit.Runtime.DeleteInstanceAsync(processId);
            }

            await inboxModel.DeleteAsync(inboxes.Select(i => i.GetId()));
            await NotifyClientsAboutInboxStatus(usersToNotify);

        }
    }
}