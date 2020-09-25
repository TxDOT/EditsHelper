using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace EditsHelper
{
    internal class SaveEditsAsyncButton : Button
    {
        private MessageBoxResult editsResult(bool hasEdits)
        {
            if (hasEdits)
            {
                var boxResult = ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                                "Save Edits?",
                                "EDITS HELPER Says...", MessageBoxButton.YesNo, MessageBoxImage.Information);
                return boxResult;
            }
            else
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                                "There Are No Unsaved Edits",
                                "EDITS HELPER Says...", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return MessageBoxResult.No;
        }
        protected override void OnClick()
        {
            var result = editsResult(Project.Current.HasEdits);
            {
                QueuedTask.Run(async () =>
                {
                    try
                    {
                        if (result == MessageBoxResult.Yes)
                        {
                            var progDlg = new ProgressDialog("Saving Edits...", "Cancel", 100, true);
                            var progSrc = new CancelableProgressorSource(progDlg);

                            do
                            {
                                await Project.Current.SaveEditsAsync();
                                progDlg.Show();
                            }
                            while (Project.Current.HasEdits);
                            progDlg.Dispose();

                            var notify = new Notification()
                            {
                                Title = "ASYNC CAGE SAYS...",
                                Message = "All Edits Saved Successfully!",
                                ImageUrl = "pack://application:,,,/EditsHelper;component/Images/cage_toast.png"
                            };

                            FrameworkApplication.AddNotification(notify);
                        }
                    }
                    catch (Exception error)
                    {
                        ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(String.Format("Save Edits Async Failed: {0}", error));
                    };
                });
            }
        }
    }
}
