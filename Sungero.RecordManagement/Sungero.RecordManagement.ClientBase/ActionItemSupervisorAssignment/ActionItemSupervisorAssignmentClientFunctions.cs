using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.RecordManagement.ActionItemSupervisorAssignment;

namespace Sungero.RecordManagement.Client
{
  partial class ActionItemSupervisorAssignmentFunctions
  {

    /// <summary>
    /// Показать диалог для принятия работ по поручению с возможностью "Принять и выполнить", "Принять", "Принять и открыть ведущее".
    /// </summary>
    /// <returns>True, если запрос был подтвержден.
    /// False, если была нажата отмена.</returns>
    public virtual bool ShowAcceptanceDialog()
    {
      var resources = Sungero.RecordManagement.ActionItemSupervisorAssignments.Resources;
      var dialog = Dialogs.CreateTaskDialog(resources.AcceptanceDialogTitle, MessageType.Question);
      var agreeAndComplete = dialog.Buttons.AddCustom(resources.AcceptanceDialogAcceptAndExecuteButtonTitle);
      var agree = dialog.Buttons.AddCustom(resources.AcceptanceDialogAcceptButtonTitle);
      var agreeAndOpen = dialog.Buttons.AddCustom(resources.AcceptanceDialogAcceptAndOpenButtonTitle);
      dialog.Buttons.AddCancel();
      dialog.Buttons.Default = agreeAndComplete;
      
      var dialogResult = dialog.Show();
      if (dialogResult == DialogButtons.Cancel)
        return false;
      
      var task = ActionItemExecutionTasks.As(_obj.Task);
      var parentAssignment = Functions.ActionItemExecutionTask.GetParentAssignment(task);
      if (dialogResult == agreeAndComplete)
      {
        // Проверить наличие подчиненных поручений.
        if (!Functions.ActionItemExecutionTask.Remote.AllOtherActionItemExecutionSubTasksAreCompleted(task))
          if (!Functions.Module.ShowAbortConfirmationDialog(parentAssignment))
            return false;

        // Добавить документы из группы "Результаты исполнения" в ведущее задание на исполнение.
        Functions.ActionItemExecutionTask.Remote.SynchronizeResultGroup(task);
        
        // Выполнить ведущее задание на исполнение.
        Functions.ActionItemExecutionTask.Remote.CompleteParentAssignment(task);
        return true;
      }
      
      if (dialogResult == agreeAndOpen)
      {
        Functions.ActionItemExecutionTask.Remote.SynchronizeResultGroup(task);
        parentAssignment.Show();
        return true;
      }
      
      return true;
    }

  }
}