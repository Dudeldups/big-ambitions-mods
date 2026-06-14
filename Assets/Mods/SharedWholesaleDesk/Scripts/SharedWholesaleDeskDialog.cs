#nullable enable
using System;
using System.Collections.Generic;
using Dialogs;
using Entities;
using Localizor;
using UI.Dialog;
using UnityEngine;

namespace SharedWholesaleDesk
{
    internal sealed class SharedWholesaleDeskDialog : Dialog
    {
        private int _productIndex;
        private int _businessIndex;
        private int _quantityIndex;
        private SharedWholesaleDeskRuntime.ProductEligibilityResult? _selectedProduct;
        private SharedWholesaleDeskRuntime.BusinessTargetRecord? _selectedBusiness;

        public SharedWholesaleDeskDialog()
        {
            var record = SharedWholesaleDeskRuntime.TryGetCurrentDeskRecord();
            npcNameKey = "dialog_wholesale_store_npc_name";

            SharedWholesaleDeskLog.Info(
                $"Opened custom shared wholesale dialog for {(record?.ServiceKind.ToString() ?? "unknown")} desk at {record?.AddressKey ?? "<no-address>"}.");

            DialogController.current.ShowEntry(BuildStartEntry(record));
        }

        private DialogEntry BuildStartEntry(SharedWholesaleDeskRuntime.PatchedServiceDeskRecord? record)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = "sharedwholesale:dialog_start_message".Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "sharedwholesale:dialog_start_confirm".Localize(),
                SecondOptionTextOverride = "sharedwholesale:dialog_start_second",
                OnConfirm = () => OpenOriginalVanillaBranch(record),
                OnSecondOption = OpenDebugCatalog,
                OnCancel = DialogController.current.CancelDialog
            };
        }

        private DialogEntry? OpenOriginalVanillaBranch(SharedWholesaleDeskRuntime.PatchedServiceDeskRecord? record)
        {
            if (record == null)
            {
                SharedWholesaleDeskLog.Warn("Original vanilla branch could not resolve the current patched wholesale desk record.");
                return BuildErrorEntry("The original wholesale desk mapping could not be resolved.");
            }

            var opened = SharedWholesaleDeskRuntime.TryOpenOriginalVanillaDialog(record);
            return opened ? null : BuildErrorEntry("The original vanilla wholesale desk dialog failed to open.");
        }

        private DialogEntry OpenDebugCatalog()
        {
            _productIndex = 0;
            _businessIndex = 0;
            _quantityIndex = 0;
            _selectedProduct = null;
            _selectedBusiness = null;
            return BuildProductEntry();
        }

        private DialogEntry BuildProductEntry()
        {
            var page = SharedWholesaleDeskRuntime.BuildProductBrowserPage(_productIndex);
            _selectedProduct = page.SelectedProduct;
            return new DialogEntry
            {
                headerKey = "sharedwholesale:dialog_catalog_header",
                messageData = "sharedwholesale:dialog_catalog_body".Localize(
                    new Dictionary<string, string> { { "body", page.Message } }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "sharedwholesale:dialog_catalog_select".Localize(),
                SecondOptionTextOverride = page.HasNextProduct ? "sharedwholesale:dialog_catalog_next_item" : null,
                OnConfirm = OpenBusinessSelection,
                OnSecondOption = page.HasNextProduct ? NextProduct : null,
                OnCancel = DialogController.current.CancelDialog
            };
        }

        private DialogEntry NextProduct()
        {
            _productIndex++;
            return BuildProductEntry();
        }

        private DialogEntry OpenBusinessSelection()
        {
            if (_selectedProduct == null)
                return BuildErrorEntry("No product is currently selected.");

            _businessIndex = 0;
            return BuildBusinessEntry();
        }

        private DialogEntry BuildBusinessEntry()
        {
            if (_selectedProduct == null)
                return BuildErrorEntry("No product is currently selected.");

            var record = SharedWholesaleDeskRuntime.TryGetCurrentDeskRecord();
            if (record == null)
                return BuildErrorEntry("The current wholesale desk mapping could not be resolved.");

            var page = SharedWholesaleDeskRuntime.BuildBusinessBrowserPage(record, _selectedProduct, _businessIndex);
            _selectedBusiness = page.SelectedBusiness;
            return new DialogEntry
            {
                headerKey = "sharedwholesale:dialog_business_header",
                messageData = "sharedwholesale:dialog_catalog_body".Localize(
                    new Dictionary<string, string> { { "body", page.Message } }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "sharedwholesale:dialog_business_select".Localize(),
                SecondOptionTextOverride = page.HasNextBusiness ? "sharedwholesale:dialog_business_next" : "sharedwholesale:dialog_catalog_back",
                OnConfirm = OpenQuantitySelection,
                OnSecondOption = page.HasNextBusiness ? NextBusiness : BuildProductEntry,
                OnCancel = DialogController.current.CancelDialog
            };
        }

        private DialogEntry NextBusiness()
        {
            _businessIndex++;
            return BuildBusinessEntry();
        }

        private DialogEntry OpenQuantitySelection()
        {
            if (_selectedBusiness == null)
                return BuildErrorEntry("No business is currently selected.");

            _quantityIndex = 0;
            return BuildQuantityEntry();
        }

        private DialogEntry BuildQuantityEntry()
        {
            if (_selectedProduct == null)
                return BuildErrorEntry("No product is currently selected.");

            var page = SharedWholesaleDeskRuntime.BuildQuantityBrowserPage(_selectedProduct, _quantityIndex);
            return new DialogEntry
            {
                headerKey = "sharedwholesale:dialog_quantity_header",
                messageData = "sharedwholesale:dialog_catalog_body".Localize(
                    new Dictionary<string, string> { { "body", page.Message } }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "sharedwholesale:dialog_quantity_order".Localize(),
                SecondOptionTextOverride = page.HasNextQuantity ? "sharedwholesale:dialog_quantity_next" : "sharedwholesale:dialog_catalog_back",
                OnConfirm = PlaceOrder,
                OnSecondOption = page.HasNextQuantity ? NextQuantity : BuildBusinessEntry,
                OnCancel = DialogController.current.CancelDialog
            };
        }

        private DialogEntry NextQuantity()
        {
            _quantityIndex++;
            return BuildQuantityEntry();
        }

        private DialogEntry PlaceOrder()
        {
            var record = SharedWholesaleDeskRuntime.TryGetCurrentDeskRecord();
            if (record == null || _selectedProduct == null || _selectedBusiness == null)
                return BuildErrorEntry("The order state could not be resolved.");

            var quantityPage = SharedWholesaleDeskRuntime.BuildQuantityBrowserPage(_selectedProduct, _quantityIndex);
            if (quantityPage.SelectedQuantity == null)
                return BuildErrorEntry("No quantity is currently selected.");

            var result = SharedWholesaleDeskRuntime.CreateModdedWholesaleContract(
                record,
                _selectedProduct,
                _selectedBusiness.Value,
                quantityPage.SelectedQuantity.Value);

            return BuildResultEntry(result.Message, result.Succeeded);
        }

        private DialogEntry ReturnToStart()
        {
            return BuildStartEntry(SharedWholesaleDeskRuntime.TryGetCurrentDeskRecord());
        }

        private DialogEntry BuildResultEntry(string message, bool succeeded)
        {
            return new DialogEntry
            {
                headerKey = succeeded ? "sharedwholesale:dialog_success_header" : "sharedwholesale:dialog_error_header",
                messageData = (succeeded
                        ? "sharedwholesale:dialog_success_message"
                        : "sharedwholesale:dialog_error_message")
                    .Localize(new Dictionary<string, string> { { "message", message } }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "sharedwholesale:dialog_error_close".Localize(),
                OnConfirm = ReturnToStart,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private static DialogEntry BuildErrorEntry(string message)
        {
            return new DialogEntry
            {
                headerKey = "sharedwholesale:dialog_error_header",
                messageData = "sharedwholesale:dialog_error_message".Localize(
                    new Dictionary<string, string> { { "message", message } }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "sharedwholesale:dialog_error_close".Localize(),
                OnConfirm = CloseErrorDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private static DialogEntry? CloseErrorDialog()
        {
            DialogController.current.FinishDialog();
            return null;
        }
    }
}
