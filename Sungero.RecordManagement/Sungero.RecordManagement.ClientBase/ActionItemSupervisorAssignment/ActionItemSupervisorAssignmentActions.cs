using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.RecordManagement.ActionItemSupervisorAssignment;

namespace Sungero.RecordManagement.Client
{
  partial class ActionItemSupervisorAssignmentActions
  {

    public virtual void Forwarded(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      _obj.Performer = _obj.ForwardedTo.SingleOrDefault();
      _obj.Save();
    }

    public virtual bool CanForwarded(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

    public virtual bool CanAgree(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

    public virtual void ForRework(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      this.CheckAssignmentAborted(e);
      
      if (!RecordManagement.Functions.ActionItemSupervisorAssignment.ValidateActionItemSupervisorAssignment(_obj, e))
        return;
      
      // Если срок вышел, добавить в диалог дополнительное описание.
      var description = Docflow.PublicFunctions.Module.CheckDeadline(ActionItemExecutionTasks.As(_obj.Task).Assignee, _obj.NewDeadline, Calendar.Now)
        ? null
        : ActionItemSupervisorAssignments.Resources.NewDeadlineLessThenTodayDescription;
      var dialogID = Constants.ActionItemExecutionTask.ActionItemSupervisorAssignmentConfirmDialogID.ForRework;
      if (!Docflow.PublicFunctions.Module.ShowConfirmationDialog(e.Action.ConfirmationMessage, description, null, dialogID))
        e.Cancel();
    }

    public virtual bool CanForRework(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

    public virtual void Agree(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      this.CheckAssignmentAborted(e);
      
      var parentAssignment = Functions.ActionItemExecutionTask.GetParentAssignment(ActionItemExecutionTasks.As(_obj.Task));
      if (parentAssignment != null &&
          parentAssignment.AccessRights.CanUpdate() &&
          parentAssignment.Result != Sungero.RecordManagement.ActionItemExecutionAssignment.Result.Done)
      {
        if (!Functions.ActionItemSupervisorAssignment.ShowAcceptanceDialog(_obj))
          e.Cancel();
      }
      else
      {
        // Замена стандартного диалога подтверждения выполнения действия.
        if (!Docflow.PublicFunctions.Module.ShowConfirmationDialog(e.Action.ConfirmationMessage, null, null,
                                                                   Constants.ActionItemExecutionTask.ActionItemSupervisorAssignmentConfirmDialogID.Agree))
          e.Cancel();
      }
    }

    public virtual bool CanAgree(Sungero.Domain.Client.CanExecuteActionArgs e)
    {
      return true;
    }

    public virtual void CheckAssignmentAborted(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      var actionItemTask = ActionItemExecutionTasks.As(_obj.Task);
      if (!Equals(actionItemTask.Supervisor, _obj.Performer) && _obj.ForwardedTo != null)
      {
        Dialogs.ShowMessage(ActionItemSupervisorAssignments.Resources.AbortAssignmentWhenSupervisorChanged, MessageType.Error);
        e.Cancel();
      }
    }
  }
}