using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Company;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.RecordManagement.ActionItemExecutionTask;
using Sungero.RecordManagement.Structures.ActionItemExecutionTask;
using Sungero.Workflow;

namespace Sungero.RecordManagement.Client
{
  partial class ActionItemExecutionTaskFunctions
  {
    /// <summary>
    /// Отключение обязательности свойств для прекращения и рестарта поручения.
    /// </summary>
    public void DisablePropertiesRequirement()
    {
      if (_obj.Assignee == null)
        _obj.State.Properties.Assignee.IsRequired = false;
      if (_obj.Deadline == null)
        _obj.State.Properties.Deadline.IsRequired = false;
    }
    
    /// <summary>
    /// Изменить простое поручение.
    /// </summary>
    /// <returns>True - процесс корректировки успешно запустился, иначе - false.</returns>
    public virtual bool ChangeSimpleActionItem()
    {
      // Инициализируем изменения поручения.
      var changes = Structures.ActionItemExecutionTask.ActionItemChanges.Create();
      changes.OldAssignee = _obj.Assignee;
      changes.OldSupervisor = _obj.Supervisor;
      changes.OldCoAssignees = _obj.CoAssignees.Select(a => a.Assignee).ToList();
      changes.NewAssignee = changes.OldAssignee;
      changes.NewSupervisor = changes.OldSupervisor;
      changes.NewCoAssignees = changes.OldCoAssignees;
      
      var dialogOpenDate = Calendar.Now;
      var helpCode = Constants.ActionItemExecutionTask.ActionItemHelpCode;
      var dialog = Dialogs.CreateInputDialog(ActionItemExecutionTasks.Resources.ChangeActionItem,
                                             ActionItemExecutionTasks.Resources.ChangeActionItemDialogInfo);
      dialog.HelpCode = helpCode;
      var supervisor = dialog.AddSelect(_obj.Info.Properties.Supervisor.LocalizedName, false, _obj.Supervisor)
        .Where(s => s.Status == CoreEntities.DatabookEntry.Status.Active);
      supervisor.IsRequired = _obj.Supervisor != null;
      var assignee = dialog.AddSelect(_obj.Info.Properties.Assignee.LocalizedName, true, _obj.Assignee)
        .Where(a => a.Status == CoreEntities.DatabookEntry.Status.Active);
      assignee.IsEnabled = _obj.ExecutionState != ExecutionState.OnControl;
      var coAssignees = dialog.AddSelectMany(_obj.Info.Properties.CoAssignees.LocalizedName, false, _obj.CoAssignees.Select(x => x.Assignee).ToArray());
      coAssignees.IsEnabled = false;
      coAssignees.IsVisible = false;
      var coAssigneesText = dialog
        .AddMultilineString(_obj.Info.Properties.CoAssignees.LocalizedName, false, GetEmployeesText(coAssignees.Value))
        .RowsCount(3);
      coAssigneesText.IsEnabled = false;
      var addCoAssignees = dialog.AddHyperlink(ActionItemExecutionTasks.Resources.AddCoAssignees);
      var deleteCoAssignees = dialog.AddHyperlink(ActionItemExecutionTasks.Resources.RemoveCoAssignees);
      var changeButton = dialog.Buttons.AddCustom(ActionItemExecutionTasks.Resources.Change);
      dialog.Buttons.AddCancel();
      
      dialog.SetOnRefresh(e =>
                          {
                            changeButton.IsEnabled = !Equals(changes.OldAssignee, changes.NewAssignee) ||
                              !Equals(changes.OldSupervisor, changes.NewSupervisor) ||
                              !changes.OldCoAssignees.SequenceEqual(changes.NewCoAssignees);
                          });
      
      // Контролер.
      supervisor.SetOnValueChanged(
        (args) =>
        {
          changes.NewSupervisor = args.NewValue;
        });
      
      // Исполнитель.
      assignee.SetOnValueChanged(
        (args) =>
        {
          changes.NewAssignee = args.NewValue;
        });
      
      // Соисполнители.
      coAssignees.SetOnValueChanged(
        (args) =>
        {
          coAssigneesText.Value = GetEmployeesText(args.NewValue);
          changes.NewCoAssignees = args.NewValue.ToList();
        });

      // Добавление соисполнителей.
      addCoAssignees.SetOnExecute(
        () =>
        {
          var selectedEmployees = Company.PublicFunctions.Employee.Remote.GetEmployees()
            .Where(ca => ca.Status == CoreEntities.DatabookEntry.Status.Active)
            .ShowSelectMany(ActionItemExecutionTasks.Resources.СhooseCoAssigneesForAdd);
          if (selectedEmployees != null && selectedEmployees.Any())
          {
            var newCoAssignees = new List<IEmployee>();
            newCoAssignees.AddRange(coAssignees.Value);
            newCoAssignees.AddRange(selectedEmployees);
            coAssignees.Value = newCoAssignees.Distinct();
          }
        });
      
      // Удаление соисполнителей.
      deleteCoAssignees.SetOnExecute(
        () =>
        {
          var selectedEmployees = coAssignees.Value.ShowSelectMany(ActionItemExecutionTasks.Resources.СhooseCoAssigneesForDelete);
          if (selectedEmployees != null && selectedEmployees.Any())
          {
            var newCoAssignees = new List<IEmployee>();
            foreach (var coAssignee in coAssignees.Value)
            {
              if (!selectedEmployees.Contains(coAssignee))
                newCoAssignees.Add(coAssignee);
            }
            coAssignees.Value = newCoAssignees;
          }
        });
      
      // Нажатие любой кнопки диалога.
      dialog.SetOnButtonClick(
        (args) =>
        {
          if (!Equals(args.Button, changeButton))
            return;
          
          var errorMessage = Functions.ActionItemExecutionTask.Remote.CheckActionItemEditInDialog(_obj, assignee.Value,
                                                                                                  dialogOpenDate);
          if (!string.IsNullOrWhiteSpace(errorMessage))
            args.AddError(errorMessage);
        });
      
      // Показ диалога.
      if (dialog.Show() == changeButton)
      {
        // Показать диалог выдачи прав на вложения из группы "Дополнительно",
        // если у кого-то из участников нет на них прав.
        var accessRightGranted = this.ShowDialogGrantAccessRights(_obj, _obj.OtherGroup.All.ToList(), changes);
        if (accessRightGranted == false)
          return false;

        // Обработать изменения поручения.
        Functions.ActionItemExecutionTask.Remote.ChangeSimpleActionItem(_obj, changes);
        
        return true;
      }
      
      return false;
    }
    
    /// <summary>
    /// Изменить составное поручение.
    /// </summary>
    /// <returns>True - процесс корректировки успешно запустился, иначе - false.</returns>
    public virtual bool ChangeCompoundActionItem()
    {
      // Инициализируем изменения поручения.
      var changes = Structures.ActionItemExecutionTask.ActionItemChanges.Create();
      changes.OldSupervisor = _obj.Supervisor;
      changes.NewSupervisor = changes.OldSupervisor;
      // Заполняем пустым списком, чтобы не падало дальше при обращении к ним.
      changes.OldCoAssignees = new List<IEmployee>();
      changes.NewCoAssignees = new List<IEmployee>();
      
      var dialogOpenDate = Calendar.Now;
      var helpCode = Constants.ActionItemExecutionTask.ActionItemHelpCode;
      var dialog = Dialogs.CreateInputDialog(ActionItemExecutionTasks.Resources.ChangeActionItem,
                                             ActionItemExecutionTasks.Resources.ChangeCompoundActionItemDialogInfo);
      dialog.HelpCode = helpCode;
      var supervisor = dialog.AddSelect(_obj.Info.Properties.Supervisor.LocalizedName, false, _obj.Supervisor)
        .Where(s => s.Status == CoreEntities.DatabookEntry.Status.Active);
      supervisor.IsRequired = _obj.Supervisor != null;
      var changeButton = dialog.Buttons.AddCustom(ActionItemExecutionTasks.Resources.Change);
      dialog.Buttons.AddCancel();
      
      dialog.SetOnRefresh(e =>
                          {
                            changeButton.IsEnabled = !Equals(changes.OldSupervisor, changes.NewSupervisor);
                          });
      
      // Контролер.
      supervisor.SetOnValueChanged(
        (args) =>
        {
          changes.NewSupervisor = args.NewValue;
        });
      
      // Нажатие любой кнопки диалога.
      dialog.SetOnButtonClick(
        (args) =>
        {
          if (!Equals(args.Button, changeButton))
            return;
          
          var errorMessage = Functions.ActionItemExecutionTask.Remote.CheckActionItemEditInDialog(_obj, null, dialogOpenDate);
          if (!string.IsNullOrWhiteSpace(errorMessage))
            args.AddError(errorMessage);
        });
      
      // Показ диалога.
      if (dialog.Show() == changeButton)
      {
        // Показать диалог выдачи прав на вложения из группы "Дополнительно",
        // если у кого-то из участников нет на них прав.
        var accessRightGranted = this.ShowDialogGrantAccessRights(_obj, _obj.OtherGroup.All.ToList(), changes);
        if (accessRightGranted == false)
          return false;
        
        // Обработать изменения поручения.
        Functions.ActionItemExecutionTask.Remote.ChangeCompoundActionItem(_obj, changes);
        
        return true;
      }
      
      return false;
    }
    
    /// <summary>
    /// Изменить пункт составного поручения.
    /// </summary>
    /// <param name="actionItemPart">Пункт составного поручения.</param>
    public virtual void ChangeCompoundActionItemPart(IActionItemExecutionTaskActionItemParts actionItemPart)
    {
      // Инициализируем изменения поручения.
      var changes = Structures.ActionItemExecutionTask.ActionItemChanges.Create();
      changes.OldAssignee = actionItemPart.Assignee;
      changes.NewAssignee = changes.OldAssignee;
      // Заполняем пустым списком, чтобы не падало дальше при обращении к ним.
      changes.OldCoAssignees = new List<IEmployee>();
      changes.NewCoAssignees = new List<IEmployee>();
      // Заполняем контролера, чтобы уведомления отправлялись корректно.
      changes.OldSupervisor = _obj.Supervisor;
      changes.NewSupervisor = _obj.Supervisor;
      
      // Информация о выбранном пункте поручения.
      var shortActionItemPart = actionItemPart.ActionItemPart ?? string.Empty;
      if (actionItemPart.Assignee.Person.ShortName.Last() != '.' && string.IsNullOrWhiteSpace(shortActionItemPart) ||
          !string.IsNullOrWhiteSpace(shortActionItemPart) && shortActionItemPart.Last() != '.')
        shortActionItemPart += '.';
      if (shortActionItemPart.Length > 50)
        shortActionItemPart = shortActionItemPart.Substring(0, 50) + "…";
      var dialogText = ActionItemExecutionTasks.Resources.ChangeCompoundActionItemPartDialogInfoFormat(actionItemPart.Assignee.Person.ShortName,
                                                                                                       shortActionItemPart);
      
      var dialogOpenDate = Calendar.Now;
      var helpCode = Constants.ActionItemExecutionTask.ActionItemHelpCode;
      var dialog = Dialogs.CreateInputDialog(ActionItemExecutionTasks.Resources.ChangeActionItemPartFormat(actionItemPart.Number),
                                             dialogText);
      dialog.HelpCode = helpCode;
      var assignee = dialog.AddSelect(actionItemPart.Info.Properties.Assignee.LocalizedName, true, actionItemPart.Assignee)
        .Where(a => a.Status == CoreEntities.DatabookEntry.Status.Active);
      var changeButton = dialog.Buttons.AddCustom(ActionItemExecutionTasks.Resources.Change);
      dialog.Buttons.AddCancel();
      
      dialog.SetOnRefresh(e =>
                          {
                            changeButton.IsEnabled = !Equals(changes.OldAssignee, changes.NewAssignee);
                          });
      
      // Контролер.
      assignee.SetOnValueChanged(
        (args) =>
        {
          changes.NewAssignee = args.NewValue;
        });
      
      // Нажатие любой кнопки диалога.
      dialog.SetOnButtonClick(
        (args) =>
        {
          if (!Equals(args.Button, changeButton))
            return;
          
          var errorMessage = Functions.ActionItemExecutionTask.Remote.CheckActionItemPartEdit(actionItemPart.ActionItemExecutionTask,
                                                                                              actionItemPart.ActionItemPartExecutionTask,
                                                                                              dialogOpenDate);
          if (!string.IsNullOrWhiteSpace(errorMessage))
            args.AddError(errorMessage);
        });
      
      // Показ диалога.
      if (dialog.Show() == changeButton)
      {
        // Показать диалог выдачи прав на вложения из группы "Дополнительно",
        // если у кого-то из участников нет на них прав.
        var actionItemPartExecutionTask = actionItemPart.ActionItemPartExecutionTask;
        var accessRightGranted = this.ShowDialogGrantAccessRights(actionItemPartExecutionTask,
                                                                  actionItemPartExecutionTask.OtherGroup.All.ToList(), changes);
        if (accessRightGranted == false)
          return;
        
        // Обработать изменения пункта поручения.
        Functions.ActionItemExecutionTask.Remote.ChangeSimpleActionItem(actionItemPartExecutionTask, changes);

        // Поменять исполнителя в гриде.
        actionItemPart.Assignee = assignee.Value;
        _obj.Save();
        
        // Показать уведомление об успешной корректировке.
        Dialogs.NotifyMessage(Sungero.RecordManagement.ActionItemExecutionTasks.Resources.ChangeActionItemSuccess);
      }
    }
    
    /// <summary>
    /// Проверить возможность корректировки поручения.
    /// </summary>
    /// <returns>True - корректировка возможна, иначе - false.</returns>
    public virtual bool CanChangeActionItem()
    {
      // Корректировать можно только поручения, созданные вручную.
      // Простые и составные поручения соисполнителям корректировать нельзя.
      if (_obj.ActionItemType != ActionItemType.Main)
        return false;
      
      // Корректировать можно, только если есть права на изменение поручения.
      if (!_obj.AccessRights.CanUpdate())
        return false;
      
      // Корректировка недоступна в десктоп-клиенте.
      if (ClientApplication.ApplicationType == ApplicationType.Desktop)
        return false;
      
      // Возможность корректировки появилась только в 3 версии схемы
      // и только для поручений, находящихся в работе.
      var schemeVersionNumberWithChangeActionItem = 3;
      return _obj.SchemeVersion >= schemeVersionNumberWithChangeActionItem &&
        _obj.Status == Sungero.Workflow.Task.Status.InProcess;
    }
    
    /// <summary>
    /// Показать диалог выдачи прав на вложения.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="attachments">Вложения.</param>
    /// <param name="changes">Изменения в поручении.</param>
    /// <returns>True, если был показан диалог (и не была нажата отмена).
    /// False, если была нажата отмена.
    /// Null, если диалог показан не был.</returns>
    [Public]
    public virtual bool? ShowDialogGrantAccessRights(ITask task,
                                                     List<Domain.Shared.IEntity> attachments,
                                                     IActionItemChanges changes)
    {
      var newPerformers = new List<IRecipient>();
      if (changes.NewAssignee != null)
        newPerformers.Add(changes.NewAssignee);
      if (changes.NewSupervisor != null)
        newPerformers.Add(changes.NewSupervisor);
      if (changes.NewCoAssignees != null && changes.NewCoAssignees.Any())
        newPerformers.AddRange(changes.NewCoAssignees);
      
      return Docflow.PublicFunctions.Module.ShowDialogGrantAccessRights(task, attachments, newPerformers);
    }
    
    private static string GetEmployeesText(IEnumerable<IEmployee> employees)
    {
      return string.Join("; ", employees.Select(x => x.Person.ShortName));
    }
  }
}