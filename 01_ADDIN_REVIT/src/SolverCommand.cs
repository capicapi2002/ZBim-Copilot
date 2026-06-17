using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace ZBIMCopilot
{
    [Transaction(TransactionMode.Manual)]
    public class SolverCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                // Mostrar el Dockable Pane de Odysseus
                DockablePaneId paneId = new DockablePaneId(OdysseusPane.PaneGuid);
                DockablePane pane = commandData.Application.GetDockablePane(paneId);
                pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ZBIM Error", ex.Message);
                return Result.Failed;
            }
        }
    }
}