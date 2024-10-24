using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderCloud.SDK;
using Headstart.Models;
using Headstart.Models.Misc;
using ordercloud.integrations.library;
using Headstart.Models.Extended;
using Headstart.Common.Models;
using Headstart.Common.Services.ShippingIntegration.Models;
using OrderCloud.Catalyst;
using Headstart.Common;
using Headstart.Models.Headstart;
using Headstart.Common.Services;
using System.Net;
using ordercloud.integrations.docebo;
using Microsoft.ApplicationInsights;
using ordercloud.integrations.docebo.Models;
using Newtonsoft.Json;

namespace Headstart.API.Commands
{
    public interface IOrderCommand
    {
        Task<Order> AcknowledgeQuoteOrder(string orderID);
        Task<ListPage<HSOrder>> ListOrdersForLocation(string locationID, ListArgs<HSOrder> listArgs, DecodedToken decodedToken);
        Task<OrderDetails> GetOrderDetails(string orderID, DecodedToken decodedToken);
        Task<List<HSShipmentWithItems>> ListHSShipmentWithItems(string orderID, DecodedToken decodedToken);
        Task<CosmosListPage<RMA>> ListRMAsForOrder(string orderID, DecodedToken decodedToken);
        Task<HSOrder> AddPromotion(string orderID, string promoCode, DecodedToken decodedToken);
        Task<HSOrder> ApplyAutomaticPromotions(string orderID);
        Task PatchOrderRequiresApprovalStatus(string orderID);
        Task<HSLineItem> SendQuoteRequestToSupplier(string orderID, string lineItemID);
        Task<HSLineItem> OverrideQuotePrice(string orderID, string lineItemID, decimal quotePrice);
        Task<ListPage<HSOrder>> ListQuoteOrders(MeUser me, QuoteStatus quoteStatus);
        Task<HSOrder> GetQuoteOrder(MeUser me, string orderID);
        Task<Boolean> ProcessPurchaseOrderLineItems(string orderID);
        Task<HSOrder> UpdatePurchaseOrder(string orderID);
        
    }

    public class OrderCommand : IOrderCommand
    {
        private readonly IOrderCloudClient _oc;
        private readonly ILocationPermissionCommand _locationPermissionCommand;
        private readonly IPromotionCommand _promotionCommand;
        private readonly IRMACommand _rmaCommand;
        private readonly AppSettings _settings;
        private readonly ISendgridService _sendgridService;
        private readonly IOrderCloudIntegrationsDoceboService _docebo;
        private readonly TelemetryClient _telemetry;

        public OrderCommand(
            ILocationPermissionCommand locationPermissionCommand,
            IOrderCloudClient oc,
            IPromotionCommand promotionCommand,
            IRMACommand rmaCommand,
            AppSettings settings,
            ISendgridService sendgridService,
            IOrderCloudIntegrationsDoceboService docebo,
            TelemetryClient telemetry
            )
        {
            _oc = oc;
            _locationPermissionCommand = locationPermissionCommand;
            _promotionCommand = promotionCommand;
            _rmaCommand = rmaCommand;
            _settings = settings;
            _sendgridService = sendgridService;
            _docebo = docebo;
            _telemetry = telemetry;
        }

        public async Task<HSLineItem> SendQuoteRequestToSupplier(string orderID, string lineItemID)
        {
            var lineItem = await _oc.LineItems.GetAsync<HSLineItem>(OrderDirection.All, orderID, lineItemID);
            var orderObject = await _oc.Orders.GetAsync<HSOrder>(OrderDirection.All, orderID);
            // SEND EMAIL NOTIFICATION TO BUYER
            await _sendgridService.SendQuoteRequestConfirmationEmail(orderObject, lineItem, orderObject.xp?.QuoteBuyerContactEmail);
            return lineItem;
        }

        public async Task<HSLineItem> OverrideQuotePrice(string orderID, string lineItemID, decimal quotePrice)
        {
            var linePatch = new PartialLineItem { UnitPrice = quotePrice };
            var updatedLineItem = await _oc.LineItems.PatchAsync<HSLineItem>(OrderDirection.All, orderID, lineItemID, linePatch);
            var orderPatch = new PartialOrder { xp = new { QuoteStatus = QuoteStatus.NeedsBuyerReview } };
            var updatedOrder = await _oc.Orders.PatchAsync<HSOrder>(OrderDirection.All, orderID, orderPatch);
            // SEND EMAIL NOTIFICATION TO BUYER
            await _sendgridService.SendQuotePriceConfirmationEmail(updatedOrder, updatedLineItem, updatedOrder.xp?.QuoteBuyerContactEmail);
            return updatedLineItem;
        }

        public async Task<ListPage<HSOrder>> ListQuoteOrders(MeUser me, QuoteStatus quoteStatus)
        {
            var supplierID = me.Supplier?.ID;
            var filters = new Dictionary<string, object>
            {
                ["xp.QuoteSupplierID"] = supplierID != null ? supplierID : "*",
                ["IsSubmitted"] = false,
                ["xp.OrderType"] = OrderType.Quote,
                ["xp.QuoteStatus"] = quoteStatus
            };
            var quoteOrders = await _oc.Orders.ListAllAsync<HSOrder>(OrderDirection.Incoming, filters: filters);
            var quoteOrdersList = new ListPage<HSOrder>()
            {
                Meta = new ListPageMeta()
                {
                    Page = 1,
                    PageSize = 1,
                    TotalCount = quoteOrders.Count,
                    TotalPages = 1,
                    ItemRange = new[] { 1, quoteOrders.Count }
                },
                Items = quoteOrders
            };
            return quoteOrdersList;
        }

        public async Task<HSOrder> GetQuoteOrder(MeUser me, string orderID)
        {
            var supplierID = me.Supplier?.ID;
            var order = await _oc.Orders.GetAsync<HSOrder>(OrderDirection.Incoming, orderID);
            if (supplierID != null && order.xp?.QuoteSupplierID != supplierID)
            {
                throw new Exception("You are not authorized to view this order.");
            }
            return order;
        }

        public async Task<Order> AcknowledgeQuoteOrder(string orderID)
        {
            var orderPatch = new PartialOrder()
            {
                xp = new
                {
                    SubmittedOrderStatus = SubmittedOrderStatus.Completed
                }
            };
            //  Need to complete sales and purchase order and patch the xp.SubmittedStatus of both orders            
            var salesOrderID = orderID.Split('-')[0];
            var completeSalesOrder = _oc.Orders.CompleteAsync(OrderDirection.Incoming, salesOrderID);
            var patchSalesOrder = _oc.Orders.PatchAsync<HSOrder>(OrderDirection.Incoming, salesOrderID, orderPatch);
            var completedSalesOrder = await completeSalesOrder;
            var patchedSalesOrder = await patchSalesOrder;

            var purchaseOrderID = $"{salesOrderID}-{patchedSalesOrder?.xp?.SupplierIDs?.FirstOrDefault()}";
            var completePurchaseOrder = _oc.Orders.CompleteAsync(OrderDirection.Outgoing, purchaseOrderID);
            var patchPurchaseOrder = _oc.Orders.PatchAsync(OrderDirection.Outgoing, purchaseOrderID, orderPatch);
            var completedPurchaseOrder = await completePurchaseOrder;
            var patchedPurchaseOrder = await patchPurchaseOrder;

            return orderID == salesOrderID ? patchedSalesOrder : patchedPurchaseOrder;
        }
       
        public async Task PatchOrderRequiresApprovalStatus(string orderID)
        {
                await PatchOrderStatus(orderID, ShippingStatus.Processing, ClaimStatus.NoClaim);
        }

        private async Task PatchOrderStatus(string orderID, ShippingStatus shippingStatus, ClaimStatus claimStatus)
        {
            var partialOrder = new PartialOrder { xp = new { ShippingStatus = shippingStatus, ClaimStatus = claimStatus } };
            await _oc.Orders.PatchAsync(OrderDirection.Incoming, orderID, partialOrder);
        }

        public async Task<ListPage<HSOrder>> ListOrdersForLocation(string locationID, ListArgs<HSOrder> listArgs, DecodedToken decodedToken)
        {
            listArgs.Filters.Add(new ListFilter("BillingAddress.ID", locationID));
            await EnsureUserCanAccessLocationOrders(locationID, decodedToken);
            return await _oc.Orders.ListAsync<HSOrder>(OrderDirection.Incoming,
                page: listArgs.Page,
                pageSize: listArgs.PageSize,
                search: listArgs.Search,
                sortBy: listArgs.SortBy.FirstOrDefault(),
                filters: listArgs.ToFilterString());
        }

        public async Task<OrderDetails> GetOrderDetails(string orderID, DecodedToken decodedToken)
        {
            var order = await _oc.Orders.GetAsync<HSOrder>(OrderDirection.Incoming, orderID);
            await EnsureUserCanAccessOrder(order, decodedToken);

            var lineItems = _oc.LineItems.ListAllAsync(OrderDirection.Incoming, orderID);
            var promotions = _oc.Orders.ListAllPromotionsAsync(OrderDirection.Incoming, orderID);
            var payments = _oc.Payments.ListAllAsync(OrderDirection.Incoming, order.ID);
            // bug in catalyst tries to list all by ID but ID doesn't exist on approval rules
            // https://github.com/ordercloud-api/ordercloud-dotnet-catalyst/issues/33
            // var approvals = _oc.Orders.ListAllApprovalsAsync(OrderDirection.Incoming, orderID);
            var approvals = await _oc.Orders.ListApprovalsAsync(OrderDirection.Incoming, orderID, pageSize: 100);
            return new OrderDetails
            {
                Order = order,
                LineItems = await lineItems,
                Promotions = await promotions,
                Payments = await payments,
                Approvals = approvals.Items
            };
        }

        public async Task<List<HSShipmentWithItems>> ListHSShipmentWithItems(string orderID, DecodedToken decodedToken)
        {
            var order = await _oc.Orders.GetAsync<HSOrder>(OrderDirection.Incoming, orderID);
            await EnsureUserCanAccessOrder(order, decodedToken);

            var lineItems = await _oc.LineItems.ListAllAsync(OrderDirection.Incoming, orderID);
            var shipments = await _oc.Orders.ListShipmentsAsync<HSShipmentWithItems>(OrderDirection.Incoming, orderID);
            var shipmentsWithItems = await Throttler.RunAsync(shipments.Items, 100, 5, (HSShipmentWithItems shipment) => GetShipmentWithItems(shipment, lineItems.ToList()));
            return shipmentsWithItems.ToList();
        }

        public async Task<CosmosListPage<RMA>> ListRMAsForOrder(string orderID, DecodedToken decodedToken)
        {
            var me = await _oc.Me.GetAsync(accessToken: decodedToken.AccessToken);
            HSOrder order = await _oc.Orders.GetAsync<HSOrder>(OrderDirection.Incoming, orderID);
            await EnsureUserCanAccessOrder(order, decodedToken);

            var listFilter = new ListFilter("SourceOrderID", orderID);
            CosmosListOptions listOptions = new CosmosListOptions() { PageSize = 100, ContinuationToken = null, Filters = { listFilter } };

            CosmosListPage<RMA> rmasOnOrder = await _rmaCommand.ListBuyerRMAs(listOptions, me.Buyer.ID);
            return rmasOnOrder;
        }

        private async Task<HSShipmentWithItems> GetShipmentWithItems(HSShipmentWithItems shipment, List<LineItem> lineItems)
        {
            var shipmentItems = await _oc.Shipments.ListItemsAsync<HSShipmentItemWithLineItem>(shipment.ID);
            shipment.ShipmentItems = shipmentItems.Items.Select(shipmentItem =>
            {
                shipmentItem.LineItem = lineItems.First(li => li.ID == shipmentItem.LineItemID);
                return shipmentItem;
            }).ToList();
            return shipment;
        }

        public async Task<HSOrder> AddPromotion(string orderID, string promoCode, DecodedToken decodedToken)
        {
            
                var promotion = await _oc.Promotions.GetAsync<LmsPromotion>(promoCode);
                var order = await _oc.Orders.GetAsync<HSOrder>(OrderDirection.Incoming, orderID);
                var user = await _oc.Users.GetAsync<User>(order.FromCompanyID, order.FromUserID);
                var userGroupAssignments = await _oc.UserGroups.ListUserAssignmentsAsync(order.FromCompanyID, null, user.ID); 

                if ((bool)(promotion.xp?.AllowAllUserGroups))
                {
                        // promo.xp.AllowAllUserGroups should always be false since promos don't apply to Buyer Group = Internal 
                        throw new CatalystBaseException("Promotion.AllowAllUserGroups", "Promotions do not apply to all user groups.");
                }
                var eligibleUserGroups = promotion.xp?.UserGroups;
                // e-learning shop Buyer Users will only be assigned to 1 UserGroup
                var userAssignment = userGroupAssignments.Items.FirstOrDefault();
                var userEligible = eligibleUserGroups.Contains(userAssignment.UserGroupID);

                Require.That(userEligible, new ErrorCode("Insufficient Access", $"User does not belong to eligible user group", HttpStatusCode.Forbidden));
                await _oc.Orders.AddPromotionAsync(OrderDirection.Incoming, orderID, promoCode);
                return await _oc.Orders.GetAsync<HSOrder>(OrderDirection.Incoming, orderID);
            
        }

        public async Task<HSOrder> ApplyAutomaticPromotions(string orderID)
        {
            await _promotionCommand.AutoApplyPromotions(orderID);
            return await _oc.Orders.GetAsync<HSOrder>(OrderDirection.Incoming, orderID);
        }
        public async Task<Boolean> ProcessPurchaseOrderLineItems(string orderID)
        {
            try
            {
                var worksheet = await _oc.IntegrationEvents.GetWorksheetAsync<HSOrderWorksheet>(
                    OrderDirection.Incoming,
                    orderID
                );
                List<DoceboItem> doceboItems = new List<DoceboItem>();
                for (int i = 0; i < worksheet.LineItems.Count; i++)
                {
                    var courseID = worksheet.LineItems[i].Product?.xp?.lms_course_id;
                    var subscriptionID = worksheet.LineItems[i].Product?.xp?.lms_SubscriptionUuid;
                    var orderOnBehalfOfEmails = worksheet.LineItems[i]?.xp?.OrderOnBehalfOf;
                    var orderOnBehalfOfIDs = new List<string>();

                    if (orderOnBehalfOfEmails != null)
                    {
                        foreach (var email in orderOnBehalfOfEmails)
                        {
                            // build up a list of ID's
                            if (email != worksheet?.Order.FromUser.Email)
                            {
                                var doceboUsers = await _docebo.SearchUsers(email);
                                orderOnBehalfOfIDs.Add(doceboUsers.data.items[0].user_id);
                            }
                            else
                            {
                                orderOnBehalfOfIDs.Add(worksheet?.Order.FromUser?.xp?.lms_user_id);
                            }
                        }
                    }
                    else
                    {
                        orderOnBehalfOfIDs.Add(worksheet?.Order.FromUser?.xp?.lms_user_id);
                    }

                    if (!String.IsNullOrEmpty(courseID))
                    {
                        foreach (var userID in orderOnBehalfOfIDs)
                        {
                            var lineItem = new DoceboItem()
                            {
                                course_id = Int32.Parse(courseID),
                                user_id = userID,
                                status = "subscribed",
                                field_2 = orderID
                            };
                            doceboItems.Add(lineItem);
                        }
                    }
                    if (!String.IsNullOrEmpty(subscriptionID))
                    {
                        var doceboSubscription = new DoceboSubscriptionRequest()
                        {
                            user_ids = new List<int>()
                        };
                        foreach (var userID in orderOnBehalfOfIDs)
                        {
                            doceboSubscription.user_ids.Add(Int32.Parse(userID));
                        }
                        try
                        {
                            var subscriptions = subscriptionID.Split("|");
                            foreach (var subscription in subscriptions)
                            {
                                await _docebo.SubscribeUsers(doceboSubscription, subscription);
                            }
                        }
                        catch (Exception ex)
                        {
                            // track in app insights
                            // to find go to Transaction Search > Event Type = Event > Filter by any of these custom properties or event name
                            var customProperties = new Dictionary<string, string>
                            {
                                { "Message", "Attempt to subscribe User to subscription failed" },
                                { "OrderID", orderID },
                                { "UserID", worksheet.Order.FromUserID },
                                { "SubscriptionID(s)", subscriptionID },
                                {
                                    "ErrorResponse",
                                    JsonConvert.SerializeObject(
                                        ex.Message,
                                        Newtonsoft.Json.Formatting.Indented
                                    )
                                }
                            };
                            _telemetry.TrackEvent(
                                "ProcessPurchaseOrderLI.DoceboSubscriptionFailed",
                                customProperties
                            );
                        }
                    }
                }

                if (doceboItems.Count > 0)
                {
                    await _docebo.UpdateUserEnrollment(doceboItems);
                }

                return true;
            }
            catch (Exception ex)
            {
                // track in app insights
                var customProperties = new Dictionary<string, string>
                {
                    {
                        "Message",
                        "Attempt to subscribe User to subscription(s), bundle(s) or ILT(s) failed"
                    },
                    { "OrderID", orderID },
                    {
                        "ErrorResponse",
                        JsonConvert.SerializeObject(ex.Message, Newtonsoft.Json.Formatting.Indented)
                    }
                };
                _telemetry.TrackEvent("ProcessPurchaseOrderLI.Failure", customProperties);
                return false;
            }
        }
        public async Task<HSOrder> UpdatePurchaseOrder (string orderID)
        {
                await ProcessPurchaseOrderLineItems(orderID);
                var partialOrder = new PartialOrder() { xp = new { ProcessedPO = true } };
                var order = await _oc.Orders.PatchAsync<HSOrder>(OrderDirection.Incoming, orderID, partialOrder);
                return order;

        }
        private async Task EnsureUserCanAccessLocationOrders(string locationID, DecodedToken decodedToken, string overrideErrorMessage = "")
        {
            var hasAccess = await _locationPermissionCommand.IsUserInAccessGroup(locationID, UserGroupSuffix.ViewAllOrders.ToString(), decodedToken);
            Require.That(hasAccess, new ErrorCode("Insufficient Access", $"User cannot access orders from this location: {locationID}", HttpStatusCode.Forbidden));
        }

        private async Task EnsureUserCanAccessOrder(HSOrder order, DecodedToken decodedToken)
        {
            /* ensures user has access to order through at least 1 of 3 methods
             * 1) user submitted the order
             * 2) user has access to all orders from the location of the billingAddressID 
             * 3) the order is awaiting approval and the user is in the approving group 
             */ 

            var isOrderSubmitter = order.FromUser.Username == decodedToken.Username;
            if (isOrderSubmitter)
            {
                return;
            }
            
            var isUserInLocationOrderAccessGroup = await _locationPermissionCommand.IsUserInAccessGroup(order.BillingAddressID, UserGroupSuffix.ViewAllOrders.ToString(), decodedToken);
            if (isUserInLocationOrderAccessGroup)
            {
                return;
            } 
            
            if(order.Status == OrderStatus.AwaitingApproval)
            {
                // logic assumes there is only one approving group per location
                var isUserInApprovalGroup = await _locationPermissionCommand.IsUserInAccessGroup(order.BillingAddressID, UserGroupSuffix.OrderApprover.ToString(), decodedToken);
                if(isUserInApprovalGroup)
                {
                    return;
                }
            }

            // if function has not been exited yet we throw an insufficient access error
            Require.That(false, new ErrorCode("Insufficient Access", $"User cannot access order {order.ID}", HttpStatusCode.Forbidden));
        }
    };
}