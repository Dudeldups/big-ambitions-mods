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
        private int _catalogPageIndex;

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
            _catalogPageIndex = 0;
            return BuildCatalogEntry();
        }

        private DialogEntry BuildCatalogEntry()
        {
            var page = SharedWholesaleDeskRuntime.BuildDebugCatalogPage(_catalogPageIndex);
            return new DialogEntry
            {
                headerKey = "sharedwholesale:dialog_catalog_header",
                messageData = "sharedwholesale:dialog_catalog_body".Localize(
                    new Dictionary<string, string> { { "body", page.Message } }),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = (page.HasNextPage
                    ? "sharedwholesale:dialog_catalog_next"
                    : "sharedwholesale:dialog_catalog_back").Localize(),
                SecondOptionTextOverride = page.HasPreviousPage ? "sharedwholesale:dialog_catalog_previous" : null,
                OnConfirm = page.HasNextPage ? NextCatalogPage : ReturnToStart,
                OnSecondOption = page.HasPreviousPage ? PreviousCatalogPage : null,
                OnCancel = DialogController.current.CancelDialog
            };
        }

        private DialogEntry NextCatalogPage()
        {
            _catalogPageIndex++;
            return BuildCatalogEntry();
        }

        private DialogEntry PreviousCatalogPage()
        {
            _catalogPageIndex = Mathf.Max(0, _catalogPageIndex - 1);
            return BuildCatalogEntry();
        }

        private DialogEntry ReturnToStart()
        {
            return BuildStartEntry(SharedWholesaleDeskRuntime.TryGetCurrentDeskRecord());
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
