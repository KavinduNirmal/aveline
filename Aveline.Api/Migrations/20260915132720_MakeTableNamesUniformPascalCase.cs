using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeTableNamesUniformPascalCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Approval_Queue_Orders_OrderId",
                table: "Approval_Queue");

            migrationBuilder.DropForeignKey(
                name: "FK_Approval_Queue_Organizations_OrganizationId",
                table: "Approval_Queue");

            migrationBuilder.DropForeignKey(
                name: "FK_Approval_Queue_Users_DecidedBy",
                table: "Approval_Queue");

            migrationBuilder.DropForeignKey(
                name: "FK_Business_Rules_Organizations_OrganizationId",
                table: "Business_Rules");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Consent_Customers_CustomerId",
                table: "Customer_Consent");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Consent_Organizations_OrganizationId",
                table: "Customer_Consent");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Events_Customers_CustomerId",
                table: "Customer_Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Events_Organizations_OrganizationId",
                table: "Customer_Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Interactions_Customers_CustomerId",
                table: "Customer_Interactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Interactions_Organizations_OrganizationId",
                table: "Customer_Interactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Interactions_Users_StaffMemberId",
                table: "Customer_Interactions");

            migrationBuilder.DropForeignKey(
                name: "FK_customer_matches_inventory_items_ItemId",
                table: "customer_matches");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Memory_Customers_CustomerId",
                table: "Customer_Memory");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Memory_Organizations_OrganizationId",
                table: "Customer_Memory");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Preferences_Customers_CustomerId",
                table: "Customer_Preferences");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Preferences_Organizations_OrganizationId",
                table: "Customer_Preferences");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Tags_Customers_CustomerId",
                table: "Customer_Tags");

            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Tags_Organizations_OrganizationId",
                table: "Customer_Tags");

            migrationBuilder.DropForeignKey(
                name: "FK_Delivery_Plans_Orders_OrderId",
                table: "Delivery_Plans");

            migrationBuilder.DropForeignKey(
                name: "FK_Delivery_Plans_Organizations_OrganizationId",
                table: "Delivery_Plans");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_images_inventory_items_ItemId",
                table: "inventory_images");

            migrationBuilder.DropForeignKey(
                name: "FK_Order_Items_Orders_OrderId",
                table: "Order_Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Order_Items_Organizations_OrganizationId",
                table: "Order_Items");

            migrationBuilder.DropForeignKey(
                name: "FK_outfit_items_inventory_items_ItemId",
                table: "outfit_items");

            migrationBuilder.DropForeignKey(
                name: "FK_outfit_items_outfit_compositions_OutfitId",
                table: "outfit_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_suppliers",
                table: "suppliers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sourcing_requests",
                table: "sourcing_requests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SignOff_Decisions",
                table: "SignOff_Decisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outfit_items",
                table: "outfit_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outfit_compositions",
                table: "outfit_compositions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Order_Items",
                table: "Order_Items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_items",
                table: "inventory_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_images",
                table: "inventory_images");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Delivery_Plans",
                table: "Delivery_Plans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customer_Tags",
                table: "Customer_Tags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customer_Preferences",
                table: "Customer_Preferences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customer_Memory",
                table: "Customer_Memory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_customer_matches",
                table: "customer_matches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customer_Interactions",
                table: "Customer_Interactions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customer_Events",
                table: "Customer_Events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Customer_Consent",
                table: "Customer_Consent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Business_Rules",
                table: "Business_Rules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Approval_Queue",
                table: "Approval_Queue");

            migrationBuilder.RenameTable(
                name: "suppliers",
                newName: "Suppliers");

            migrationBuilder.RenameTable(
                name: "sourcing_requests",
                newName: "SourcingRequests");

            migrationBuilder.RenameTable(
                name: "SignOff_Decisions",
                newName: "SignOffDecisions");

            migrationBuilder.RenameTable(
                name: "outfit_items",
                newName: "OutfitItems");

            migrationBuilder.RenameTable(
                name: "outfit_compositions",
                newName: "OutfitCompositions");

            migrationBuilder.RenameTable(
                name: "Order_Items",
                newName: "OrderItems");

            migrationBuilder.RenameTable(
                name: "inventory_items",
                newName: "InventoryItems");

            migrationBuilder.RenameTable(
                name: "inventory_images",
                newName: "InventoryImages");

            migrationBuilder.RenameTable(
                name: "Delivery_Plans",
                newName: "DeliveryPlans");

            migrationBuilder.RenameTable(
                name: "Customer_Tags",
                newName: "CustomerTags");

            migrationBuilder.RenameTable(
                name: "Customer_Preferences",
                newName: "CustomerPreferences");

            migrationBuilder.RenameTable(
                name: "Customer_Memory",
                newName: "CustomerMemory");

            migrationBuilder.RenameTable(
                name: "customer_matches",
                newName: "CustomerMatches");

            migrationBuilder.RenameTable(
                name: "Customer_Interactions",
                newName: "CustomerInteractions");

            migrationBuilder.RenameTable(
                name: "Customer_Events",
                newName: "CustomerEvents");

            migrationBuilder.RenameTable(
                name: "Customer_Consent",
                newName: "CustomerConsent");

            migrationBuilder.RenameTable(
                name: "Business_Rules",
                newName: "BusinessRules");

            migrationBuilder.RenameTable(
                name: "Approval_Queue",
                newName: "ApprovalQueue");

            migrationBuilder.RenameIndex(
                name: "IX_SignOff_Decisions_OrganizationId",
                table: "SignOffDecisions",
                newName: "IX_SignOffDecisions_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_SignOff_Decisions_MessageId",
                table: "SignOffDecisions",
                newName: "IX_SignOffDecisions_MessageId");

            migrationBuilder.RenameIndex(
                name: "IX_outfit_items_ItemId",
                table: "OutfitItems",
                newName: "IX_OutfitItems_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_Order_Items_OrganizationId",
                table: "OrderItems",
                newName: "IX_OrderItems_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Order_Items_OrderId",
                table: "OrderItems",
                newName: "IX_OrderItems_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Order_Items_ItemId",
                table: "OrderItems",
                newName: "IX_OrderItems_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_items_OrgId_Status_DeletedAt_Category_Color",
                table: "InventoryItems",
                newName: "IX_InventoryItems_OrgId_Status_DeletedAt_Category_Color");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_images_OrgId_ItemId",
                table: "InventoryImages",
                newName: "IX_InventoryImages_OrgId_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_Delivery_Plans_TrackingNumber",
                table: "DeliveryPlans",
                newName: "IX_DeliveryPlans_TrackingNumber");

            migrationBuilder.RenameIndex(
                name: "IX_Delivery_Plans_Status",
                table: "DeliveryPlans",
                newName: "IX_DeliveryPlans_Status");

            migrationBuilder.RenameIndex(
                name: "IX_Delivery_Plans_OrganizationId",
                table: "DeliveryPlans",
                newName: "IX_DeliveryPlans_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Delivery_Plans_OrderId",
                table: "DeliveryPlans",
                newName: "IX_DeliveryPlans_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Tags_OrganizationId",
                table: "CustomerTags",
                newName: "IX_CustomerTags_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Tags_CustomerId_Tag",
                table: "CustomerTags",
                newName: "IX_CustomerTags_CustomerId_Tag");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Tags_CustomerId",
                table: "CustomerTags",
                newName: "IX_CustomerTags_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Preferences_OrganizationId",
                table: "CustomerPreferences",
                newName: "IX_CustomerPreferences_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Preferences_CustomerId_PreferenceKey",
                table: "CustomerPreferences",
                newName: "IX_CustomerPreferences_CustomerId_PreferenceKey");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Preferences_CustomerId",
                table: "CustomerPreferences",
                newName: "IX_CustomerPreferences_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Memory_OrganizationId",
                table: "CustomerMemory",
                newName: "IX_CustomerMemory_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Memory_CustomerId",
                table: "CustomerMemory",
                newName: "IX_CustomerMemory_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Memory_Category",
                table: "CustomerMemory",
                newName: "IX_CustomerMemory_Category");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Interactions_StaffMemberId",
                table: "CustomerInteractions",
                newName: "IX_CustomerInteractions_StaffMemberId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Interactions_OrganizationId",
                table: "CustomerInteractions",
                newName: "IX_CustomerInteractions_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Interactions_CustomerId_CreatedAt",
                table: "CustomerInteractions",
                newName: "IX_CustomerInteractions_CustomerId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Interactions_CustomerId",
                table: "CustomerInteractions",
                newName: "IX_CustomerInteractions_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Interactions_Channel",
                table: "CustomerInteractions",
                newName: "IX_CustomerInteractions_Channel");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Events_OrganizationId",
                table: "CustomerEvents",
                newName: "IX_CustomerEvents_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Events_EventDate",
                table: "CustomerEvents",
                newName: "IX_CustomerEvents_EventDate");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Events_CustomerId",
                table: "CustomerEvents",
                newName: "IX_CustomerEvents_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Consent_OrganizationId_CustomerId",
                table: "CustomerConsent",
                newName: "IX_CustomerConsent_OrganizationId_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Customer_Consent_CustomerId",
                table: "CustomerConsent",
                newName: "IX_CustomerConsent_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_Business_Rules_RuleType",
                table: "BusinessRules",
                newName: "IX_BusinessRules_RuleType");

            migrationBuilder.RenameIndex(
                name: "IX_Business_Rules_OrganizationId",
                table: "BusinessRules",
                newName: "IX_BusinessRules_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Business_Rules_IsActive",
                table: "BusinessRules",
                newName: "IX_BusinessRules_IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_Approval_Queue_Status",
                table: "ApprovalQueue",
                newName: "IX_ApprovalQueue_Status");

            migrationBuilder.RenameIndex(
                name: "IX_Approval_Queue_OrganizationId",
                table: "ApprovalQueue",
                newName: "IX_ApprovalQueue_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_Approval_Queue_OrderId",
                table: "ApprovalQueue",
                newName: "IX_ApprovalQueue_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Approval_Queue_DecidedBy",
                table: "ApprovalQueue",
                newName: "IX_ApprovalQueue_DecidedBy");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Suppliers",
                table: "Suppliers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SourcingRequests",
                table: "SourcingRequests",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SignOffDecisions",
                table: "SignOffDecisions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OutfitItems",
                table: "OutfitItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OutfitCompositions",
                table: "OutfitCompositions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderItems",
                table: "OrderItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_InventoryItems",
                table: "InventoryItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_InventoryImages",
                table: "InventoryImages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeliveryPlans",
                table: "DeliveryPlans",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CustomerTags",
                table: "CustomerTags",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CustomerPreferences",
                table: "CustomerPreferences",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CustomerMemory",
                table: "CustomerMemory",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CustomerMatches",
                table: "CustomerMatches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CustomerInteractions",
                table: "CustomerInteractions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CustomerEvents",
                table: "CustomerEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CustomerConsent",
                table: "CustomerConsent",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BusinessRules",
                table: "BusinessRules",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApprovalQueue",
                table: "ApprovalQueue",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalQueue_Orders_OrderId",
                table: "ApprovalQueue",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalQueue_Organizations_OrganizationId",
                table: "ApprovalQueue",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalQueue_Users_DecidedBy",
                table: "ApprovalQueue",
                column: "DecidedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessRules_Organizations_OrganizationId",
                table: "BusinessRules",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerConsent_Customers_CustomerId",
                table: "CustomerConsent",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerConsent_Organizations_OrganizationId",
                table: "CustomerConsent",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerEvents_Customers_CustomerId",
                table: "CustomerEvents",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerEvents_Organizations_OrganizationId",
                table: "CustomerEvents",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerInteractions_Customers_CustomerId",
                table: "CustomerInteractions",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerInteractions_Organizations_OrganizationId",
                table: "CustomerInteractions",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerInteractions_Users_StaffMemberId",
                table: "CustomerInteractions",
                column: "StaffMemberId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerMatches_InventoryItems_ItemId",
                table: "CustomerMatches",
                column: "ItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerMemory_Customers_CustomerId",
                table: "CustomerMemory",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerMemory_Organizations_OrganizationId",
                table: "CustomerMemory",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPreferences_Customers_CustomerId",
                table: "CustomerPreferences",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPreferences_Organizations_OrganizationId",
                table: "CustomerPreferences",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerTags_Customers_CustomerId",
                table: "CustomerTags",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerTags_Organizations_OrganizationId",
                table: "CustomerTags",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryPlans_Orders_OrderId",
                table: "DeliveryPlans",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryPlans_Organizations_OrganizationId",
                table: "DeliveryPlans",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryImages_InventoryItems_ItemId",
                table: "InventoryImages",
                column: "ItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Orders_OrderId",
                table: "OrderItems",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Organizations_OrganizationId",
                table: "OrderItems",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OutfitItems_InventoryItems_ItemId",
                table: "OutfitItems",
                column: "ItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OutfitItems_OutfitCompositions_OutfitId",
                table: "OutfitItems",
                column: "OutfitId",
                principalTable: "OutfitCompositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalQueue_Orders_OrderId",
                table: "ApprovalQueue");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalQueue_Organizations_OrganizationId",
                table: "ApprovalQueue");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalQueue_Users_DecidedBy",
                table: "ApprovalQueue");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessRules_Organizations_OrganizationId",
                table: "BusinessRules");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerConsent_Customers_CustomerId",
                table: "CustomerConsent");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerConsent_Organizations_OrganizationId",
                table: "CustomerConsent");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerEvents_Customers_CustomerId",
                table: "CustomerEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerEvents_Organizations_OrganizationId",
                table: "CustomerEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerInteractions_Customers_CustomerId",
                table: "CustomerInteractions");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerInteractions_Organizations_OrganizationId",
                table: "CustomerInteractions");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerInteractions_Users_StaffMemberId",
                table: "CustomerInteractions");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerMatches_InventoryItems_ItemId",
                table: "CustomerMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerMemory_Customers_CustomerId",
                table: "CustomerMemory");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerMemory_Organizations_OrganizationId",
                table: "CustomerMemory");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPreferences_Customers_CustomerId",
                table: "CustomerPreferences");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPreferences_Organizations_OrganizationId",
                table: "CustomerPreferences");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerTags_Customers_CustomerId",
                table: "CustomerTags");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerTags_Organizations_OrganizationId",
                table: "CustomerTags");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryPlans_Orders_OrderId",
                table: "DeliveryPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryPlans_Organizations_OrganizationId",
                table: "DeliveryPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryImages_InventoryItems_ItemId",
                table: "InventoryImages");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Orders_OrderId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Organizations_OrganizationId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OutfitItems_InventoryItems_ItemId",
                table: "OutfitItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OutfitItems_OutfitCompositions_OutfitId",
                table: "OutfitItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Suppliers",
                table: "Suppliers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SourcingRequests",
                table: "SourcingRequests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SignOffDecisions",
                table: "SignOffDecisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OutfitItems",
                table: "OutfitItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OutfitCompositions",
                table: "OutfitCompositions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderItems",
                table: "OrderItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InventoryItems",
                table: "InventoryItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InventoryImages",
                table: "InventoryImages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DeliveryPlans",
                table: "DeliveryPlans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CustomerTags",
                table: "CustomerTags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CustomerPreferences",
                table: "CustomerPreferences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CustomerMemory",
                table: "CustomerMemory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CustomerMatches",
                table: "CustomerMatches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CustomerInteractions",
                table: "CustomerInteractions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CustomerEvents",
                table: "CustomerEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CustomerConsent",
                table: "CustomerConsent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BusinessRules",
                table: "BusinessRules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApprovalQueue",
                table: "ApprovalQueue");

            migrationBuilder.RenameTable(
                name: "Suppliers",
                newName: "suppliers");

            migrationBuilder.RenameTable(
                name: "SourcingRequests",
                newName: "sourcing_requests");

            migrationBuilder.RenameTable(
                name: "SignOffDecisions",
                newName: "SignOff_Decisions");

            migrationBuilder.RenameTable(
                name: "OutfitItems",
                newName: "outfit_items");

            migrationBuilder.RenameTable(
                name: "OutfitCompositions",
                newName: "outfit_compositions");

            migrationBuilder.RenameTable(
                name: "OrderItems",
                newName: "Order_Items");

            migrationBuilder.RenameTable(
                name: "InventoryItems",
                newName: "inventory_items");

            migrationBuilder.RenameTable(
                name: "InventoryImages",
                newName: "inventory_images");

            migrationBuilder.RenameTable(
                name: "DeliveryPlans",
                newName: "Delivery_Plans");

            migrationBuilder.RenameTable(
                name: "CustomerTags",
                newName: "Customer_Tags");

            migrationBuilder.RenameTable(
                name: "CustomerPreferences",
                newName: "Customer_Preferences");

            migrationBuilder.RenameTable(
                name: "CustomerMemory",
                newName: "Customer_Memory");

            migrationBuilder.RenameTable(
                name: "CustomerMatches",
                newName: "customer_matches");

            migrationBuilder.RenameTable(
                name: "CustomerInteractions",
                newName: "Customer_Interactions");

            migrationBuilder.RenameTable(
                name: "CustomerEvents",
                newName: "Customer_Events");

            migrationBuilder.RenameTable(
                name: "CustomerConsent",
                newName: "Customer_Consent");

            migrationBuilder.RenameTable(
                name: "BusinessRules",
                newName: "Business_Rules");

            migrationBuilder.RenameTable(
                name: "ApprovalQueue",
                newName: "Approval_Queue");

            migrationBuilder.RenameIndex(
                name: "IX_SignOffDecisions_OrganizationId",
                table: "SignOff_Decisions",
                newName: "IX_SignOff_Decisions_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_SignOffDecisions_MessageId",
                table: "SignOff_Decisions",
                newName: "IX_SignOff_Decisions_MessageId");

            migrationBuilder.RenameIndex(
                name: "IX_OutfitItems_ItemId",
                table: "outfit_items",
                newName: "IX_outfit_items_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_OrganizationId",
                table: "Order_Items",
                newName: "IX_Order_Items_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_OrderId",
                table: "Order_Items",
                newName: "IX_Order_Items_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_ItemId",
                table: "Order_Items",
                newName: "IX_Order_Items_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_InventoryItems_OrgId_Status_DeletedAt_Category_Color",
                table: "inventory_items",
                newName: "IX_inventory_items_OrgId_Status_DeletedAt_Category_Color");

            migrationBuilder.RenameIndex(
                name: "IX_InventoryImages_OrgId_ItemId",
                table: "inventory_images",
                newName: "IX_inventory_images_OrgId_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryPlans_TrackingNumber",
                table: "Delivery_Plans",
                newName: "IX_Delivery_Plans_TrackingNumber");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryPlans_Status",
                table: "Delivery_Plans",
                newName: "IX_Delivery_Plans_Status");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryPlans_OrganizationId",
                table: "Delivery_Plans",
                newName: "IX_Delivery_Plans_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryPlans_OrderId",
                table: "Delivery_Plans",
                newName: "IX_Delivery_Plans_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerTags_OrganizationId",
                table: "Customer_Tags",
                newName: "IX_Customer_Tags_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerTags_CustomerId_Tag",
                table: "Customer_Tags",
                newName: "IX_Customer_Tags_CustomerId_Tag");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerTags_CustomerId",
                table: "Customer_Tags",
                newName: "IX_Customer_Tags_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerPreferences_OrganizationId",
                table: "Customer_Preferences",
                newName: "IX_Customer_Preferences_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerPreferences_CustomerId_PreferenceKey",
                table: "Customer_Preferences",
                newName: "IX_Customer_Preferences_CustomerId_PreferenceKey");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerPreferences_CustomerId",
                table: "Customer_Preferences",
                newName: "IX_Customer_Preferences_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerMemory_OrganizationId",
                table: "Customer_Memory",
                newName: "IX_Customer_Memory_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerMemory_CustomerId",
                table: "Customer_Memory",
                newName: "IX_Customer_Memory_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerMemory_Category",
                table: "Customer_Memory",
                newName: "IX_Customer_Memory_Category");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerInteractions_StaffMemberId",
                table: "Customer_Interactions",
                newName: "IX_Customer_Interactions_StaffMemberId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerInteractions_OrganizationId",
                table: "Customer_Interactions",
                newName: "IX_Customer_Interactions_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerInteractions_CustomerId_CreatedAt",
                table: "Customer_Interactions",
                newName: "IX_Customer_Interactions_CustomerId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerInteractions_CustomerId",
                table: "Customer_Interactions",
                newName: "IX_Customer_Interactions_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerInteractions_Channel",
                table: "Customer_Interactions",
                newName: "IX_Customer_Interactions_Channel");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerEvents_OrganizationId",
                table: "Customer_Events",
                newName: "IX_Customer_Events_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerEvents_EventDate",
                table: "Customer_Events",
                newName: "IX_Customer_Events_EventDate");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerEvents_CustomerId",
                table: "Customer_Events",
                newName: "IX_Customer_Events_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerConsent_OrganizationId_CustomerId",
                table: "Customer_Consent",
                newName: "IX_Customer_Consent_OrganizationId_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_CustomerConsent_CustomerId",
                table: "Customer_Consent",
                newName: "IX_Customer_Consent_CustomerId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessRules_RuleType",
                table: "Business_Rules",
                newName: "IX_Business_Rules_RuleType");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessRules_OrganizationId",
                table: "Business_Rules",
                newName: "IX_Business_Rules_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessRules_IsActive",
                table: "Business_Rules",
                newName: "IX_Business_Rules_IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_ApprovalQueue_Status",
                table: "Approval_Queue",
                newName: "IX_Approval_Queue_Status");

            migrationBuilder.RenameIndex(
                name: "IX_ApprovalQueue_OrganizationId",
                table: "Approval_Queue",
                newName: "IX_Approval_Queue_OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_ApprovalQueue_OrderId",
                table: "Approval_Queue",
                newName: "IX_Approval_Queue_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_ApprovalQueue_DecidedBy",
                table: "Approval_Queue",
                newName: "IX_Approval_Queue_DecidedBy");

            migrationBuilder.AddPrimaryKey(
                name: "PK_suppliers",
                table: "suppliers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sourcing_requests",
                table: "sourcing_requests",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SignOff_Decisions",
                table: "SignOff_Decisions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outfit_items",
                table: "outfit_items",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outfit_compositions",
                table: "outfit_compositions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Order_Items",
                table: "Order_Items",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_items",
                table: "inventory_items",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_images",
                table: "inventory_images",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Delivery_Plans",
                table: "Delivery_Plans",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customer_Tags",
                table: "Customer_Tags",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customer_Preferences",
                table: "Customer_Preferences",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customer_Memory",
                table: "Customer_Memory",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_customer_matches",
                table: "customer_matches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customer_Interactions",
                table: "Customer_Interactions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customer_Events",
                table: "Customer_Events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Customer_Consent",
                table: "Customer_Consent",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Business_Rules",
                table: "Business_Rules",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Approval_Queue",
                table: "Approval_Queue",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Approval_Queue_Orders_OrderId",
                table: "Approval_Queue",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Approval_Queue_Organizations_OrganizationId",
                table: "Approval_Queue",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Approval_Queue_Users_DecidedBy",
                table: "Approval_Queue",
                column: "DecidedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Business_Rules_Organizations_OrganizationId",
                table: "Business_Rules",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Consent_Customers_CustomerId",
                table: "Customer_Consent",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Consent_Organizations_OrganizationId",
                table: "Customer_Consent",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Events_Customers_CustomerId",
                table: "Customer_Events",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Events_Organizations_OrganizationId",
                table: "Customer_Events",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Interactions_Customers_CustomerId",
                table: "Customer_Interactions",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Interactions_Organizations_OrganizationId",
                table: "Customer_Interactions",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Interactions_Users_StaffMemberId",
                table: "Customer_Interactions",
                column: "StaffMemberId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_customer_matches_inventory_items_ItemId",
                table: "customer_matches",
                column: "ItemId",
                principalTable: "inventory_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Memory_Customers_CustomerId",
                table: "Customer_Memory",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Memory_Organizations_OrganizationId",
                table: "Customer_Memory",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Preferences_Customers_CustomerId",
                table: "Customer_Preferences",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Preferences_Organizations_OrganizationId",
                table: "Customer_Preferences",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Tags_Customers_CustomerId",
                table: "Customer_Tags",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Tags_Organizations_OrganizationId",
                table: "Customer_Tags",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Delivery_Plans_Orders_OrderId",
                table: "Delivery_Plans",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Delivery_Plans_Organizations_OrganizationId",
                table: "Delivery_Plans",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_images_inventory_items_ItemId",
                table: "inventory_images",
                column: "ItemId",
                principalTable: "inventory_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Order_Items_Orders_OrderId",
                table: "Order_Items",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Order_Items_Organizations_OrganizationId",
                table: "Order_Items",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_outfit_items_inventory_items_ItemId",
                table: "outfit_items",
                column: "ItemId",
                principalTable: "inventory_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_outfit_items_outfit_compositions_OutfitId",
                table: "outfit_items",
                column: "OutfitId",
                principalTable: "outfit_compositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
