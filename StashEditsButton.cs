using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Editing.Attributes;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace EditsHelper
{
    internal class StashEditsButton : Button
    {
        protected override void OnClick()
        {
            if (Project.Current.HasEdits)
            {
                QueuedTask.Run(() =>
                {
                    try
                    {
                        //let user know what's happening
                        var progDlgBld = new ProgressDialog("Building Your Stash...", "Cancel", 1000, false);
                        var progDlgStash = new ProgressDialog("Stashing Your Edits...", "Cancel", 1000, false);
                        progDlgBld.Show();

                        //make two dictionaries to get changes made during edit session
                        var beforeDict = new Dictionary<long, object>();
                        var afterDict = new Dictionary<long, object>();
                        var editsList = new List<long>();

                        //inspector to check TRE table
                        var inspector = new Inspector(false);

                        //get edit ops from undo stack
                        var opMgr = MapView.Active.Map.OperationManager;

                        //undo edit ops to see when edit session began
                        opMgr.UndoAsync(opMgr.FindUndoOperations(o => o.Category == "Editing").Count, "Editing");

                        // Get the TRE layer and filter by system username
                        var userName = Environment.UserName;
                        var layers = MapView.Active.Map.GetLayersAsFlattenedList().OfType<FeatureLayer>();
                        var featLayer = layers.First(l => l.Name.ToLower().Contains("txdot_roadways_edits"));
                        QueryFilter queryFilter = new QueryFilter
                        {
                            WhereClause = string.Format("EDIT_NM = '{0}'", userName)
                        };
                        var selectedFeatures = featLayer.Select(queryFilter);
                        var oidList = selectedFeatures.GetObjectIDs();

                        //get oids and timestamps for "before"
                        foreach (var oid in oidList)
                        {
                            inspector.Load(featLayer, oid);
                            beforeDict.Add(oid, inspector["EDIT_DT"]);
                        }

                        //add undo ops back and get oids and timestamps for "after"
                        opMgr.RedoAsync(opMgr.FindRedoOperations(o => o.Category == "Editing").Count, "Editing");

                        foreach (var oid in oidList)
                        {
                            inspector.Load(featLayer, oid);
                            afterDict.Add(oid, inspector["EDIT_DT"]);
                            editsList.Add(oid);
                        }


                        //OPTION 1: FILTER DICTS AND ADD OIDS TO NEW LIST

                        //now filter two dictionaries to get most recent timestamp and write oids to a new list
                        //check to make sure keys match
                        //if not, add new oids to list
                        //next, compare each value to get most recent timestamp and add to list

                        //OPTION 2: EDIT DUMP
                        //get all records for afterDict and dump to gdb, sort out later


                        // Setting up new folder in project directory for the Stash
                        var dirPath = Project.Current.HomeFolderPath + @"\MyStash";
                        if (!Directory.Exists(dirPath))
                        {
                            Directory.CreateDirectory(dirPath);
                        }

                        string outGDBPath = dirPath;
                        //string outGDBName = Environment.UserName + DateTime.Now.ToString("yyyyMMddHHmmss") + ".gdb";
                        string outGDBName = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds + ".gdb";
                        var gdbParams = Geoprocessing.MakeValueArray(outGDBPath, outGDBName);
                        Geoprocessing.ExecuteToolAsync("management.CreateFileGDB", gdbParams,
                            null, new CancelableProgressorSource(progDlgBld).Progressor, GPExecuteToolFlags.Default);
                        progDlgBld.Dispose();


                        //create new fc and add field for original oid 
                        string featLyrPath = featLayer.GetFeatureClass().GetDatastore().GetPath().AbsolutePath;
                        string newFCPath = outGDBPath + @"\" + outGDBName;
                        string newFCName = @"unsaved_edits_schema";
                        var newFCParams = Geoprocessing.MakeValueArray(
                            newFCPath, newFCName, "POLYLINE", featLyrPath, "ENABLED", featLyrPath);
                        Geoprocessing.ExecuteToolAsync("management.CreateFeatureclass", newFCParams);

                        var newFieldPath = newFCPath + @"\" + newFCName;
                        var fieldParams = Geoprocessing.MakeValueArray(newFieldPath, "TRE_OBJECTID", "SHORT");
                        Geoprocessing.ExecuteToolAsync("management.AddField", fieldParams);

                        //string featLyrPath = featLayer.GetFeatureClass().GetDatastore().GetPath().AbsolutePath;
                        var featLyrPathFull = System.IO.Path.GetFullPath(featLyrPath);
                        string inFeats = System.IO.Path.Combine(featLyrPathFull, featLayer.Name);
                        string outFCPath = outGDBPath + @"\" + outGDBName;
                        string outFCName = @"unsaved_edits";
                        string whereClause = string.Format("OBJECTID IN ({0})", String.Join(",", editsList));
                        var fcParams = Geoprocessing.MakeValueArray(inFeats, outFCPath, outFCName, whereClause);
                        Geoprocessing.ExecuteToolAsync("conversion.FeatureClassToFeatureClass", fcParams);

                        //string mergeOutPath = outFCPath + @"\" + outFCName;
                        //var mergeParams = Geoprocessing.MakeValueArray("["+mergeOutPath+","+newFieldPath+"]", mergeOutPath);
                        //Geoprocessing.ExecuteToolAsync("management.Merge", mergeParams);
                        //write unsaved edits to newly created fc
                        //the edits list will eventually be filtered to only include edited records
                        //string featLyrPath = featLayer.GetFeatureClass().GetDatastore().GetPath().AbsolutePath;
                        //var featLyrPathFull = System.IO.Path.GetFullPath(featLyrPath);
                        //string inFeats = System.IO.Path.Combine(featLyrPathFull, featLayer.Name);
                        //string outFCPath = outGDBPath + @"\" + outGDBName;
                        //string outFCName = @"unsaved_edits";
                        //string whereClause = string.Format("OBJECTID IN ({0})", String.Join(",", editsList));
                        //var fcParams = Geoprocessing.MakeValueArray(inFeats, outFCPath, outFCName, whereClause);
                        //Geoprocessing.ExecuteToolAsync("conversion.FeatureClassToFeatureClass", fcParams,
                        //    null, new CancelableProgressorSource(progDlgStash).Progressor, GPExecuteToolFlags.Default);
                        //progDlgStash.Show();

                        //need to add field "OG_OBJECT_ID" to preserve original value (will be writing back to TRE table using SQL)
                        //AddField(in_table, field_name, field_type)
                        //var outFieldPath = outFCPath + @"\" + outFCName;
                        //var fieldParams = Geoprocessing.MakeValueArray(outFieldPath, "TRE_OBJECTID", "SHORT");
                        //Geoprocessing.ExecuteToolAsync("management.AddField", fieldParams);

                        //if edits are stashed, discard edits after writing to gdb
                        Project.Current.DiscardEditsAsync();
                        progDlgStash.Dispose();

                    }
                    catch (Exception e)
                    {
                        ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(e.Message);
                    }
                });
            }
            else
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    "There Are No Unsaved Edits",
                    "EDITS HELPER Says...", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
