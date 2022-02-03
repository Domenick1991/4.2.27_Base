using System;
using System.Collections.Generic;
using System.Linq;
using CommonLibrary;
using Sungero.Company;
using Sungero.Content;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Domain.Shared;
using Sungero.RecordManagement.ActionItemExecutionTask;
using Sungero.RecordManagement.Structures.ActionItemExecutionTask;
using Sungero.Security;
using Sungero.Workflow;
using Sungero.Workflow.Task;
using DeclensionCase = Sungero.Core.DeclensionCase;

namespace Sungero.RecordManagement.Server
{
  partial class ActionItemExecutionTaskFunctions
  {

    #region Предметное отображение
    
    /// <summary>
    /// Построить модель состояния главного поручения.
    /// </summary>
    /// <returns>Схема модели состояния.</returns>
    [Public, Remote(IsPure = true)]
    public string GetStateViewXml()
    {
      return this.GetStateView().ToString();
    }
    
    /// <summary>
    /// Построить модель состояния главного поручения.
    /// </summary>
    /// <returns>Контрол состояния.</returns>
    [Remote(IsPure = true)]
    public Sungero.Core.StateView GetStateView()
    {
      // Определить главное поручение и построить его состояние.
      var mainActionItemExecutionTask = this.GetMainActionItemExecutionTask();

      var stateViewModel = Structures.ActionItemExecutionTask.StateViewModel.Create();
      stateViewModel.Tasks = new List<IActionItemExecutionTask>() { mainActionItemExecutionTask };
      stateViewModel = GetAllActionItems(stateViewModel);
      return GetActionItemStateView(mainActionItemExecutionTask, _obj, stateViewModel, null, null);
    }

    /// <summary>
    /// Найти самое верхнее поручение.
    /// </summary>
    /// <returns>Самое верхнее поручение.</returns>
    public IActionItemExecutionTask GetMainActionItemExecutionTask()
    {
      var mainActionItemExecutionTask = _obj;
      ITask currentTask = _obj;
      while (currentTask.ParentTask != null || currentTask.ParentAssignment != null)
      {
        currentTask = currentTask.ParentTask ?? currentTask.ParentAssignment.Task;
        if (ActionItemExecutionTasks.Is(currentTask))
          mainActionItemExecutionTask = ActionItemExecutionTasks.As(currentTask);
      }
      return mainActionItemExecutionTask;
    }
    
    /// <summary>
    /// Построить модель состояния главного поручения.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Контрол состояния.</returns>
    public Sungero.Core.StateView GetStateView(Sungero.Docflow.IOfficialDocument document)
    {
      // Проекты резолюций будут добавлены вместе с задачей на рассмотрение.
      if (_obj.IsDraftResolution == true)
        return StateView.Create();
      
      if (!_obj.DocumentsGroup.OfficialDocuments.Any(d => Equals(document, d)))
        return StateView.Create();
      
      return GetActionItemStateView(_obj, null, null, null, null);
    }
    
    /// <summary>
    /// Построить модель состояния поручения.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="openedTask">Новое подпоручение.</param>
    /// <param name="stateViewModel">Модель предметного отображения.</param>
    /// <param name="draftAssignee">Исполнитель в черновике.</param>
    /// <param name="draftActionItem">Поручение в черновике.</param>
    /// <param name="draftDeadline">Срок в черновике.</param>
    /// <param name="draftNumber">Номер поручения в черновике составного.</param>
    /// <param name="skipResolutionBlock">Пропустить блок резолюции.</param>
    /// <param name="withHighlight">Выделять цветом основной блок.</param>
    /// <returns>Модель состояния.</returns>
    [Public]
    public static Sungero.Core.StateView GetActionItemStateView(IActionItemExecutionTask task,
                                                                IActionItemExecutionTask openedTask,
                                                                Structures.ActionItemExecutionTask.IStateViewModel stateViewModel,
                                                                IEmployee draftAssignee = null,
                                                                string draftActionItem = "",
                                                                DateTime? draftDeadline = null,
                                                                int? draftNumber = null,
                                                                bool skipResolutionBlock = false,
                                                                bool withHighlight = true)
    {
      var stateView = StateView.Create();
      
      if (task == null && openedTask == null)
        return stateView;
      
      if (stateViewModel == null)
        stateViewModel = Structures.ActionItemExecutionTask.StateViewModel.Create();

      if (stateViewModel.Tasks == null || stateViewModel.Tasks.Count == 0)
      {
        stateViewModel.Tasks = new List<IActionItemExecutionTask>() { task };
        stateViewModel = GetAllActionItems(stateViewModel);
      }
      
      var isDraft = true;
      if (task != null)
        isDraft = task.Status == Workflow.Task.Status.Draft;
      
      // Стили.
      var headerStyle = Docflow.PublicFunctions.Module.CreateHeaderStyle(isDraft);
      var performerDeadlineStyle = Docflow.PublicFunctions.Module.CreatePerformerDeadlineStyle(isDraft);
      var boldStyle = Docflow.PublicFunctions.Module.CreateStyle(true, isDraft, false);
      var grayStyle = Docflow.PublicFunctions.Module.CreateStyle(false, isDraft, true);
      var labelStyle = Docflow.PublicFunctions.Module.CreateStyle(false, isDraft, false);
      var separatorStyle = Docflow.PublicFunctions.Module.CreateSeparatorStyle();

      if (stateViewModel.StatusesCache == null)
        stateViewModel.StatusesCache = new Dictionary<Enumeration?, string>();

      // Добавить блок по резолюции, если поручение в рамках рассмотрения.
      // Блок добавить только для самого верхнего поручения.
      if (task != null && task.MainTask != null && !skipResolutionBlock &&
          Equals(task, Functions.ActionItemExecutionTask.GetMainActionItemExecutionTask(task)) && DocumentReviewTasks.Is(task.MainTask))
      {
        StateView reviewState;
        if (DocumentReviewTasks.Is(task.MainTask))
        {
          if (task.ParentAssignment != null && DocumentReviewTasks.Is(task.ParentAssignment.Task))
            // Добавить блок информации о резолюции без добавления дополнительного блока поручения.
            reviewState = Functions.DocumentReviewTask.GetDocumentReviewStateView(DocumentReviewTasks.As(task.ParentAssignment.Task), false);
          else
            reviewState = Functions.DocumentReviewTask.GetStateView(DocumentReviewTasks.As(task.MainTask));
          
          foreach (var block in reviewState.Blocks)
            stateView.AddBlock(block);
        }
        else if (Docflow.ApprovalTasks.Is(task.MainTask))
        {
          Functions.ActionItemExecutionTask.AddReviewBlock(task, stateView, stateViewModel.Tasks, stateViewModel.Assignments);
        }
      }
      
      var main = false;
      var additional = false;
      var component = false;
      var underControl = false;
      var hasCoAssignees = false;
      var isCompound = false;
      if (task != null)
      {
        main = task.ActionItemType == ActionItemType.Main;
        additional = task.ActionItemType == ActionItemType.Additional;
        component = task.ActionItemType == ActionItemType.Component;
        underControl = task.IsUnderControl == true;
        hasCoAssignees = task.CoAssignees.Any();
        isCompound = task.IsCompoundActionItem == true;
      }
      else
      {
        var isCompoundDraftActionItem = openedTask.IsCompoundActionItem == true;
        additional = !isCompoundDraftActionItem;
        component = isCompoundDraftActionItem;
        underControl = !isCompoundDraftActionItem || openedTask.IsUnderControl == true;
        isDraft = true;
      }

      // Не выводить задачу, если она была стартована до последнего рестарта главной, если это не черновик.
      if (!isDraft)
      {
        var parentTask = Tasks.Null;
        if (task.ActionItemType == ActionItemType.Component)
          parentTask = task.ParentTask;
        else if (task.ActionItemType == ActionItemType.Additional)
          parentTask = task.ParentAssignment.Task;
        
        if (parentTask != null && parentTask.Started.HasValue && task.Started.HasValue && parentTask.Started > task.Started)
          return StateView.Create();
      }

      // Добавить заголовок с информацией по отправителю поручения.
      if (main && task != null)
      {
        var text = ActionItemExecutionTasks.Resources.StateViewActionItemOnExecution;
        if (task.ParentAssignment != null && ActionItemExecutionAssignments.Is(task.ParentAssignment))
          text = ActionItemExecutionTasks.Resources.StateViewSubordinateActionItemSent;
        var comment = Docflow.PublicFunctions.Module.GetFormatedUserText(text);
        
        if (task.Started.HasValue)
          Docflow.PublicFunctions.OfficialDocument
            .AddUserActionBlock(stateView, task.Author, comment, task.Started.Value, task, string.Empty, task.StartedBy);
        else
          Docflow.PublicFunctions.OfficialDocument
            .AddUserActionBlock(stateView, task.Author, Docflow.ApprovalTasks.Resources.StateViewTaskDrawCreated, task.Created.Value, task, string.Empty, task.Author);
      }
      
      var taskBlock = stateView.AddBlock();
      
      if (task != null && !task.State.IsInserted)
        taskBlock.Entity = task;
      
      if (Equals(task, openedTask) && withHighlight)
        Docflow.PublicFunctions.Module.MarkBlock(taskBlock);
      
      // Для поручения соисполнителю сменить иконку.
      if (additional)
        taskBlock.AssignIcon(ActionItemExecutionAssignments.Info.Actions.CreateChildActionItem, StateBlockIconSize.Large);
      else if (isCompound)
        taskBlock.AssignIcon(ActionItemExecutionTasks.Resources.CompoundActionItem, StateBlockIconSize.Large);
      else if (taskBlock.Entity != null)
        taskBlock.AssignIcon(StateBlockIconType.OfEntity, StateBlockIconSize.Large);
      else
        taskBlock.AssignIcon(Docflow.ApprovalRuleBases.Resources.ActionItemTask, StateBlockIconSize.Large);

      // Статус.
      var status = GetStatusInfo(task, stateViewModel.StatusesCache);
      
      // Для непрочитанных заданий указать это.
      if (task != null && task.Status == Workflow.Task.Status.InProcess)
      {
        var actionItemExecution = stateViewModel.Assignments
          .Where(a => Equals(a.Task.Id, task.Id) && ActionItemExecutionAssignments.Is(a))
          .OrderByDescending(a => a.Created)
          .FirstOrDefault();
        if (actionItemExecution != null && actionItemExecution.IsRead == false)
          status = Docflow.ApprovalTasks.Resources.StateViewUnRead.ToString();
      }
      if (!string.IsNullOrWhiteSpace(status))
        Docflow.PublicFunctions.Module.AddInfoToRightContent(taskBlock, status, labelStyle);
      
      // Заголовок.
      var header = GetHeader(task, additional, component, hasCoAssignees, isCompound, openedTask, draftNumber);
      taskBlock.AddLabel(header, headerStyle);
      taskBlock.AddLineBreak();
      
      // Задержка исполнения.
      var deadline = task != null ? GetDeadline(task, isCompound) : draftDeadline;
      var deadlineLabel = deadline.HasValue ? Docflow.PublicFunctions.Module.ToShortDateShortTime(deadline.Value.ToUserTime()) : ActionItemExecutionTasks.Resources.StateViewNotSpecified;
      if (deadline.HasValue && task != null &&
          (task.ExecutionState == ExecutionState.OnExecution ||
           task.ExecutionState == ExecutionState.OnControl ||
           task.ExecutionState == ExecutionState.OnRework))
        Docflow.PublicFunctions.OfficialDocument.AddDeadlineHeaderToRight(taskBlock, deadline.Value, task.Assignee ?? Users.Current);
      
      // Добавить информацию по главному поручению, поручению соисполнителю и подпоручений составного.
      if (!isCompound)
      {
        // Кому.
        var assignee = task != null ? task.Assignee : draftAssignee;
        var assigneeName = assignee != null ? Company.PublicFunctions.Employee.GetShortName(assignee, false) : ActionItemExecutionTasks.Resources.StateViewNotSpecified;
        var performerInfo = string.Format("{0}: {1}", Docflow.OfficialDocuments.Resources.StateViewTo, assigneeName);
        taskBlock.AddLabel(performerInfo, performerDeadlineStyle);
        
        // Срок.
        var deadlineInfo = string.Format(" {0}: {1} ", Docflow.OfficialDocuments.Resources.StateViewDeadline, deadlineLabel);
        taskBlock.AddLabel(deadlineInfo, performerDeadlineStyle);
        
        // Контролёр.
        var supervisor = task != null ? task.Supervisor : openedTask.Assignee;
        if (underControl && !component && supervisor != null)
        {
          var supervisorInfo = string.Format(" {0}: {1}", Docflow.OfficialDocuments.Resources.StateViewSupervisor, Company.PublicFunctions.Employee.GetShortName(supervisor, false));
          taskBlock.AddLabel(supervisorInfo.Trim(), performerDeadlineStyle);
        }
        
        // Разделитель.
        taskBlock.AddLineBreak();
        taskBlock.AddLabel(Docflow.Constants.Module.SeparatorText, separatorStyle);
        taskBlock.AddLineBreak();
        taskBlock.AddEmptyLine(Docflow.Constants.Module.EmptyLineMargin);
        
        var actionItem = task != null ? task.ActionItem : draftActionItem;
        
        // Отчет по исполнению поручения и текст поручения.
        var report = GetReportInfo(task, stateViewModel.Assignments);
        if (!string.IsNullOrWhiteSpace(report))
        {
          taskBlock.AddLabel(Docflow.PublicFunctions.Module.GetFormatedUserText(actionItem), grayStyle);
          
          taskBlock.AddLineBreak();
          taskBlock.AddLabel(report, labelStyle);
        }
        else
        {
          taskBlock.AddLabel(Docflow.PublicFunctions.Module.GetFormatedUserText(actionItem), labelStyle);
        }
        
        // Добавить подпоручения.
        AddAssignmentTasks(taskBlock, task, openedTask, stateViewModel);
      }
      else
      {
        // Добавить информацию по главному поручению составного.
        // Общий срок.
        if (task.FinalDeadline.HasValue)
          deadline = task.FinalDeadline;
        
        if (deadline.HasValue)
          taskBlock.AddLabel(string.Format("{0}: {1}",
                                           Docflow.OfficialDocuments.Resources.StateViewFinalDeadline,
                                           Docflow.PublicFunctions.Module.ToShortDateShortTime(deadline.Value.ToUserTime())),
                             performerDeadlineStyle);
        
        // Контролёр.
        var supervisor = task != null ? task.Supervisor : openedTask.Supervisor;
        if (underControl && !component && supervisor != null)
        {
          var supervisorInfo = string.Format(" {0}: {1}", Docflow.OfficialDocuments.Resources.StateViewSupervisor, Company.PublicFunctions.Employee.GetShortName(supervisor, false));
          taskBlock.AddLabel(supervisorInfo.Trim(), performerDeadlineStyle);
        }
        
        // Разделитель.
        taskBlock.AddLineBreak();
        taskBlock.AddLabel(Docflow.Constants.Module.SeparatorText, separatorStyle);
        taskBlock.AddLineBreak();
        
        // Общий текст составного поручения.
        var actionItem = task != null ? task.ActionItem : draftActionItem;
        
        if (task != null && task.Status != Sungero.Workflow.Task.Status.Draft && task.Status != Sungero.Workflow.Task.Status.InProcess)
        {
          taskBlock.AddLabel(Docflow.PublicFunctions.Module.GetFormatedUserText(actionItem), grayStyle);
        }
        else
        {
          taskBlock.AddLabel(Docflow.PublicFunctions.Module.GetFormatedUserText(actionItem), labelStyle);
        }
        
        // Добавить подпоручения составного поручения и подпоручения к ним.
        AddComponentSubTasks(taskBlock, task, openedTask, stateViewModel);
        taskBlock.NeedGroupChildren = true;
      }
      
      taskBlock.IsExpanded = false;
      
      // Раскрыть поручение, если оно в работе, на приёмке, это черновик или это открытое поручение.
      if (isDraft || task.Status == Workflow.Task.Status.InProcess ||
          task.Status == Workflow.Task.Status.UnderReview || Equals(task, openedTask))
        taskBlock.IsExpanded = true;
      
      // Если есть развернутые подчиненные поручения, то развернуть и это.
      if (taskBlock.ChildBlocks.Where(c => c.IsExpanded == true).Any())
        taskBlock.IsExpanded = true;

      return stateView;
    }
    
    /// <summary>
    /// Заполнение модели контрола состояния задачи на исполнение поручения.
    /// </summary>
    /// <param name="model">Модель контрола состояния.</param>
    /// <returns>Заполненная (полностью или частично) модель контрола состояния.</returns>
    public static Structures.ActionItemExecutionTask.IStateViewModel GetAllActionItems(Structures.ActionItemExecutionTask.IStateViewModel model)
    {
      if (model.Tasks == null)
        model.Tasks = new List<IActionItemExecutionTask>();
      if (model.Assignments == null)
        model.Assignments = new List<IAssignment>();
      
      var tasksIds = model.Tasks.Select(p => p.Id).ToList();
      var assignmentsIds = model.Assignments.Select(p => p.Id).ToList();
      
      // Подзадачи - пункты составного поручения.
      var subtasks = ActionItemExecutionTasks.GetAll(t => t.ParentTask != null && tasksIds.Contains(t.ParentTask.Id) && !tasksIds.Contains(t.Id)).ToList();
      model.Tasks.AddRange(subtasks);

      // Подзадачи - подчиненные поручения и поручения соисполнителям.
      var assignments = Assignments.GetAll(a => tasksIds.Contains(a.Task.Id) && !assignmentsIds.Contains(a.Id)).ToList();
      model.Assignments.AddRange(assignments);
      assignmentsIds = assignments.Select(a => a.Id).ToList();
      var assignmentSubtasks = ActionItemExecutionTasks.GetAll(t => t.ParentAssignment != null &&
                                                               assignmentsIds.Contains(t.ParentAssignment.Id)).ToList();
      model.Tasks.AddRange(assignmentSubtasks);
      
      if (subtasks.Any() || assignmentSubtasks.Any())
        GetAllActionItems(model);

      Logger.DebugFormat("ActionItemsView: tasks count: {0}", model.Tasks.Count.ToString());
      return model;
    }

    /// <summary>
    /// Получить статус выполнения поручения.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="statusesCache">Кэш статусов.</param>
    /// <returns>Статус.</returns>
    private static string GetStatusInfo(IActionItemExecutionTask task, Dictionary<Enumeration?, string> statusesCache)
    {
      Enumeration? status = null;
      if (task == null || task.Status == Workflow.Task.Status.Draft)
      {
        status = Workflow.Task.Status.Draft;
      }
      else if (task.ExecutionState != null && task.IsCompoundActionItem != true)
      {
        status = task.ExecutionState == ExecutionState.OnRework ? ExecutionState.OnExecution : task.ExecutionState;
      }
      else if (task.Status == Workflow.Task.Status.InProcess)
      {
        status = ExecutionState.OnExecution;
      }
      else if (task.Status == Workflow.Task.Status.Aborted)
      {
        status = ExecutionState.Aborted;
      }
      else if (task.Status == Workflow.Task.Status.Suspended)
      {
        status = Workflow.AssignmentBase.Status.Suspended;
      }
      else if (task.Status == Workflow.Task.Status.Completed)
      {
        status = ExecutionState.Executed;
      }
      
      return GetLocalizedValue(status, statusesCache);
    }

    private static string GetLocalizedValue(Enumeration? value, Dictionary<Enumeration?, string> statusesCache)
    {
      string localizedStatus = string.Empty;
      if (!statusesCache.TryGetValue(value.Value, out localizedStatus))
      {
        localizedStatus = value == Workflow.Task.Status.Draft ?
          Workflow.Tasks.Info.Properties.Status.GetLocalizedValue(value) :
          ActionItemExecutionTasks.Info.Properties.ExecutionState.GetLocalizedValue(value);
        statusesCache.Add(value.Value, localizedStatus);
      }

      return localizedStatus;
    }
    
    /// <summary>
    /// Получить заголовок блока поручения.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="additional">Задача соисполнителю.</param>
    /// <param name="component">Задача составного поручения.</param>
    /// <param name="hasCoAssignees">Есть соисполнители.</param>
    /// <param name="isCompound">Составное поручение.</param>
    /// <param name="openedTask">Черновик.</param>
    /// <param name="number">Номер подпункта поручения.</param>
    /// <returns>Заголовок.</returns>
    private static string GetHeader(IActionItemExecutionTask task, bool additional, bool component, bool hasCoAssignees, bool isCompound,
                                    IActionItemExecutionTask openedTask, int? number)
    {
      var header = ActionItemExecutionTasks.Resources.StateViewActionItem;
      if (additional)
        header = ActionItemExecutionTasks.Resources.StateViewActionItemForCoAssignee;
      
      if (isCompound)
        header = ActionItemExecutionTasks.Resources.StateViewCompoundActionItem;
      
      if (hasCoAssignees && !additional)
        header = ActionItemExecutionTasks.Resources.StateViewActionItemForResponsible;
      
      if (component)
      {
        if (number != null)
          return string.Format("{0}{1}", ActionItemExecutionTasks.Resources.StateViewActionItemPart, number);
        else
          header = ActionItemExecutionTasks.Resources.StateViewActionItemPart;
      }
      
      return header;
    }

    /// <summary>
    /// Получить срок поручения.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="isCompound">Составное.</param>
    /// <returns>Срок.</returns>
    private static DateTime? GetDeadline(IActionItemExecutionTask task, bool isCompound)
    {
      // Срок обычного поручения.
      if (task.MaxDeadline.HasValue)
        return task.MaxDeadline.Value;
      
      // Срок составного поручения.
      if (isCompound)
        return task.ActionItemParts.Select(p => p.Deadline ?? task.FinalDeadline).Max();
      
      return null;
    }
    
    /// <summary>
    /// Получить отчет по поручению.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="assignments">Задания.</param>
    /// <returns>Отчет.</returns>
    private static string GetReportInfo(IActionItemExecutionTask task, List<IAssignment> assignments)
    {
      if (task == null)
        return string.Empty;
      
      var actionItemExecution = assignments.Where(a => ActionItemExecutionAssignments.Is(a))
        .Where(a => Equals(a.Task.Id, task.Id))
        .OrderByDescending(a => a.Created)
        .FirstOrDefault();
      
      if (actionItemExecution != null && actionItemExecution.Status == Workflow.AssignmentBase.Status.Completed)
        return string.Format("{0}: {1}", ActionItemExecutionTasks.Resources.StateViewReport,
                             Docflow.PublicFunctions.Module.GetFormatedUserText(actionItemExecution.ActiveText));
      
      return string.Empty;
    }
    
    /// <summary>
    /// Добавить блоки подпоручений.
    /// </summary>
    /// <param name="block">Блок.</param>
    /// <param name="task">Задача.</param>
    /// <param name="openedTask">Новое подпоручение.</param>
    /// <param name="stateViewModel">Модель предметного отображения.</param>
    private static void AddAssignmentTasks(StateBlock block, IActionItemExecutionTask task, IActionItemExecutionTask openedTask,
                                           Structures.ActionItemExecutionTask.IStateViewModel stateViewModel)
    {
      if (task == null)
        return;
      
      // Добавить ещё не созданные подзадачи черновика.
      if (Equals(task, openedTask) && openedTask.Status == Workflow.Task.Status.Draft)
      {
        var childBlocks = openedTask.CoAssignees
          .SelectMany(a => Functions.ActionItemExecutionTask
                      .GetActionItemStateView(null, openedTask, stateViewModel, a.Assignee, openedTask.ActionItem, openedTask.MaxDeadline, null, false, true)
                      .Blocks);
        foreach (var childBlock in childBlocks)
          block.AddChildBlock(childBlock);
        block.IsExpanded = true;
        return;
      }
      
      var subTasks = stateViewModel.Tasks
        .Where(t => t.ParentAssignment != null && Equals(t.ParentAssignment.Task.Id, task.Id))
        .Where(t => t.Started >= task.Started)
        .OrderBy(t => t.Started)
        .ToList();
      
      // Добавить вывод черновика подпоручения.
      if (openedTask != null && openedTask.ParentAssignment != null &&
          Equals(task, openedTask.ParentAssignment.Task) &&
          !subTasks.Any(st => Equals(openedTask, st)))
        subTasks.Add(openedTask);
      
      var blocks = subTasks.SelectMany(t => GetActionItemStateView(t, openedTask, stateViewModel).Blocks);
      foreach (var childBlock in blocks)
        block.AddChildBlock(childBlock);
      block.IsExpanded = subTasks.Any(t => t.Status == Workflow.Task.Status.InProcess || t.Status == Workflow.Task.Status.Draft) ||
        block.ChildBlocks.Any(b => b.IsExpanded);
    }
    
    /// <summary>
    /// Добавить блоки подпоручений составного поручения.
    /// </summary>
    /// <param name="stateBlock">Схема.</param>
    /// <param name="task">Задача.</param>
    /// <param name="openedTask">Черновик.</param>
    /// <param name="stateViewModel">Модель предметного отображения.</param>
    private static void AddComponentSubTasks(StateBlock stateBlock, IActionItemExecutionTask task, IActionItemExecutionTask openedTask,
                                             Structures.ActionItemExecutionTask.IStateViewModel stateViewModel)
    {
      if (task == null)
        return;
      
      // Добавить ещё не созданные подзадачи черновика.
      if (Equals(task, openedTask) && openedTask.Status == Workflow.Task.Status.Draft)
      {
        var draftTaskParts = openedTask.ActionItemParts;
        var childBlocks = draftTaskParts.OrderBy(a => a.Number)
          .SelectMany(a => Functions.ActionItemExecutionTask.GetActionItemStateView(
            null, openedTask, stateViewModel, a.Assignee,
            string.IsNullOrEmpty(a.ActionItemPart) ? openedTask.ActionItem : a.ActionItemPart,
            a.Deadline ?? openedTask.FinalDeadline,
            a.Number, false, true)
                      .Blocks);
        foreach (var block in childBlocks)
          stateBlock.AddChildBlock(block);
        stateBlock.IsExpanded = true;
        return;
      }
      
      foreach (var partTask in task.ActionItemParts.OrderBy(pt => pt.Number))
      {
        var currentPartTask = partTask.ActionItemPartExecutionTask;
        if (currentPartTask == null || stateBlock.ChildBlocks.Any(b => b.HasEntity(currentPartTask)))
          continue;
        
        var childBlocks = GetActionItemStateView(currentPartTask, openedTask, stateViewModel, currentPartTask.Assignee, draftNumber: partTask.Number).Blocks;
        foreach (var block in childBlocks)
          stateBlock.AddChildBlock(block);
      }
    }
    
    /// <summary>
    /// Добавить блок информации о рассмотрении документа руководителем.
    /// </summary>
    /// <param name="stateView">Схема представления.</param>
    /// <param name="tasks">Задачи.</param>
    /// <param name="assignments">Задания.</param>
    /// <returns>Полученный блок.</returns>
    public Sungero.Core.StateBlock AddReviewBlock(Sungero.Core.StateView stateView, List<IActionItemExecutionTask> tasks, List<IAssignment> assignments)
    {
      var reviewAssignmentBase = assignments.Where(a => Docflow.ApprovalReviewAssignments.Is(a))
        .Where(a => Equals(a.Task.Id, _obj.MainTask.Id))
        .OrderByDescending(a => a.Created)
        .FirstOrDefault();
      
      if (reviewAssignmentBase == null)
        return null;

      var reviewAssignment = Docflow.ApprovalReviewAssignments.As(reviewAssignmentBase);
      
      // Добавить блок информации по отправителю.
      var text = Docflow.ApprovalTasks.Resources.StateViewDocumentSentForApproval;
      var task = reviewAssignment.Task;
      Docflow.PublicFunctions.OfficialDocument
        .AddUserActionBlock(stateView, task.Author, text, task.Started.Value, task, string.Empty, task.StartedBy);
      
      var author = Docflow.PublicFunctions.OfficialDocument.GetAuthor(reviewAssignment.Performer, reviewAssignment.CompletedBy);
      var actionItems = tasks
        .Where(t => t.ParentAssignment != null && Equals(t.ParentAssignment.Task.Id, reviewAssignment.Task.Id) && t.Status != Workflow.Task.Status.Draft)
        .OrderBy(t => t.Started);
      var isCompleted = reviewAssignment.Status == Workflow.AssignmentBase.Status.Completed;

      var headerStyle = Docflow.PublicFunctions.Module.CreateHeaderStyle();
      var performerStyle = Docflow.PublicFunctions.Module.CreatePerformerDeadlineStyle();
      var separatorStyle = Docflow.PublicFunctions.Module.CreateSeparatorStyle();
      
      // Добавить блок. Установить иконку и сущность.
      var block = stateView.AddBlock();
      block.Entity = reviewAssignment;
      if (isCompleted)
        block.AssignIcon(reviewAssignment.Info.Actions.AddResolution, StateBlockIconSize.Large);
      else
        block.AssignIcon(StateBlockIconType.OfEntity, StateBlockIconSize.Large);

      // Рассмотрение руководителем ещё в работе.
      if (!isCompleted)
      {
        // Добавить заголовок.
        block.AddLabel(Docflow.Resources.StateViewDocumentReview, headerStyle);
        block.AddLineBreak();
        Docflow.PublicFunctions.Module.AddInfoToRightContent(block, Docflow.ApprovalTasks.Info.Properties.Status.GetLocalizedValue(reviewAssignment.Status));
        var employeeName = Employees.Is(reviewAssignment.Performer) ?
          Company.PublicFunctions.Employee.GetShortName(Employees.As(reviewAssignment.Performer), false) :
          reviewAssignment.Performer.Name;
        var headerText = string.Format("{0}: {1} ",
                                       Docflow.Resources.StateViewAddressee,
                                       employeeName);
        
        if (reviewAssignment.Deadline != null)
        {
          var deadlineText = string.Format(" {0}: {1}",
                                           Docflow.OfficialDocuments.Resources.StateViewDeadline,
                                           Docflow.PublicFunctions.Module.ToShortDateShortTime(reviewAssignment.Deadline.Value.ToUserTime()));
          headerText = headerText + deadlineText;
        }
        
        block.AddLabel(headerText, performerStyle);
        
        Docflow.PublicFunctions.OfficialDocument.AddDeadlineHeaderToRight(block, reviewAssignment.Deadline.Value, reviewAssignment.Performer);
      }
      else
      {
        // Рассмотрение завершено.
        // Добавить заголовок.
        var resolutionDate = Docflow.PublicFunctions.Module.ToShortDateShortTime(reviewAssignment.Completed.Value.ToUserTime());
        block.AddLabel(Docflow.Resources.StateViewResolution, headerStyle);
        block.AddLineBreak();
        block.AddLabel(string.Format("{0}: {1} {2}: {3}",
                                     RecordManagement.DocumentReviewTasks.Resources.StateViewAuthor,
                                     author,
                                     Docflow.OfficialDocuments.Resources.StateViewDate,
                                     resolutionDate), performerStyle);
        block.AddLineBreak();
        block.AddLabel(Docflow.Constants.Module.SeparatorText, separatorStyle);
        block.AddLineBreak();
        block.AddEmptyLine(Docflow.Constants.Module.EmptyLineMargin);
        
        // Если поручения не созданы, значит, рассмотрение выполнено с результатом "Вынести резолюцию" или "Принято к сведению".
        // В старых задачах поручение и рассмотрение не связаны, поэтому обрабатываем такие случаи как резолюцию.
        if (!actionItems.Any())
        {
          var comment = Docflow.PublicFunctions.Module.GetFormatedUserText(reviewAssignment.Texts.Last().Body);
          block.AddLabel(comment);
          block.AddLineBreak();
        }
        else
        {
          // Добавить информацию по каждому поручению.
          foreach (var actionItem in actionItems)
          {
            if (actionItem.IsCompoundActionItem == true)
            {
              foreach (var item in actionItem.ActionItemParts)
              {
                AddActionItemInfo(block, item.ActionItemPartExecutionTask, author);
              }
            }
            else
            {
              AddActionItemInfo(block, actionItem, author);
            }
          }
        }
      }
      return block;
    }
    
    /// <summary>
    /// Добавить информацию о созданном поручении в резолюцию.
    /// </summary>
    /// <param name="block">Блок.</param>
    /// <param name="actionItem">Поручение.</param>
    /// <param name="author">Автор.</param>
    public static void AddActionItemInfo(Sungero.Core.StateBlock block, IActionItemExecutionTask actionItem, string author)
    {
      block.AddEmptyLine(Docflow.Constants.Module.EmptyLineMargin);
      
      block.AddLabel(Docflow.PublicFunctions.Module.GetFormatedUserText(actionItem.ActiveText));
      block.AddLineBreak();
      
      // Исполнители.
      var performerStyle = Sungero.Docflow.PublicFunctions.Module.CreatePerformerDeadlineStyle();
      var info = string.Empty;
      if (actionItem.CoAssignees.Any())
        info += string.Format("{0}: {1}, {2}: {3}",
                              Docflow.Resources.StateViewResponsible,
                              Company.PublicFunctions.Employee.GetShortName(actionItem.Assignee, false),
                              Docflow.Resources.StateViewCoAssignees,
                              string.Join(", ", actionItem.CoAssignees.Select(c => Company.PublicFunctions.Employee.GetShortName(c.Assignee, false))));
      else
        info += string.Format("{0}: {1}", Docflow.Resources.StateViewAssignee, Company.PublicFunctions.Employee.GetShortName(actionItem.Assignee, false));
      
      // Срок.
      if (actionItem.MaxDeadline.HasValue)
        info += string.Format(" {0}: {1}", Docflow.OfficialDocuments.Resources.StateViewDeadline, Docflow.PublicFunctions.Module.ToShortDateShortTime(actionItem.MaxDeadline.Value.ToUserTime()));
      
      // Контролер.
      if (actionItem.IsUnderControl == true)
      {
        info += string.Format(" {0}: {1}", Docflow.OfficialDocuments.Resources.StateViewSupervisor, Company.PublicFunctions.Employee.GetShortName(actionItem.Supervisor, false));
      }
      
      block.AddLabel(info, performerStyle);
      block.AddLineBreak();
      block.AddLineBreak();
    }
    
    #endregion
    
    #region Изменение поручения
    
    /// <summary>
    /// Изменить простое поручение.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    [Public, Remote]
    public virtual void ChangeSimpleActionItem(IActionItemChanges changes)
    {
      // Заполнить текущие задания на исполнение и приемку.
      var oldExecutionAssignment = this.GetActualActionItemExecutionAssignment();
      var oldSupervisorAssignment = this.GetActualActionItemSupervisorAssignment();
      
      // Изменить значения в задаче.
      this.UpdateActionItemTask(_obj, changes);

      var onEdit = ActionItemExecutionTasks.GetAll().Where(a => a.Id == _obj.Id && a.OnEdit == true).Any();
      if (!onEdit)
      {
        _obj.OnEdit = true;
      }
      else
      {
        throw AppliedCodeException.Create(ActionItemExecutionTasks.Resources.ActionItemIsAlreadyInChangingProcess);
      }
      
      _obj.Save();
      
      // Инициализировать адресатов.
      var addressees = new List<IUser>();
      
      try
      {
        // Получить список заинтересованных в изменении поручения для отправки уведомления.
        // Находится здесь, чтобы учитывать состояние поручения до изменений.
        addressees = this.GetActionItemChangeNotificationAddressees(changes, oldExecutionAssignment != null);
        
        // Переадресовать измененные задания.
        this.ForwardChangedAssignments(changes, oldExecutionAssignment, oldSupervisorAssignment);
        
        // Прекратить неактуальные запросы отчетов от контролера, от отв. исполнителя, к отв. исполнителю.
        this.AbortReportRequestTasks(changes, oldExecutionAssignment);
      }
      catch
      {
        _obj.OnEdit = false;
        _obj.Save();
        throw AppliedCodeException.Create(ActionItemExecutionTasks.Resources.ActionItemChangeError);
      }
      
      // Создание подзадачек соисполнителям и пунктов поручений.
      _obj.Blocks.Block110.Execute();
      
      // Прекращение подзадач удаленным соисполнителям (поручения, запросы отчетов, запросы продления сроков).
      this.AbortDeletedCoAssigneeTasks(changes);
      
      // Разослать уведомления об изменении поручения.
      this.SendActionItemChangeNotifications(changes, addressees);
    }
    
    /// <summary>
    /// Изменить составное поручение.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    [Public, Remote]
    public virtual void ChangeCompoundActionItem(IActionItemChanges changes)
    {
      // Протащить изменения в основную задачу.
      this.UpdateActionItemTask(_obj, changes);
      var onEdit = ActionItemExecutionTasks.GetAll().Where(a => a.Id == _obj.Id && a.OnEdit == true).Any();
      if (!onEdit)
      {
        _obj.OnEdit = true;
      }
      else
      {
        throw AppliedCodeException.Create(ActionItemExecutionTasks.Resources.ActionItemIsAlreadyInChangingProcess);
      }
      _obj.Save();
      
      // Инициализировать адресатов.
      var addressees = new List<IUser>();
      
      try
      {
        // Получить список заинтересованных в изменении поручения для отправки уведомления.
        // Находится здесь, чтобы учитывать состояние поручения до изменений.
        addressees = this.GetCompoundActionItemChangeNotificationAddressees(changes);

        // Протащить изменения в пункты поручения, которые еще в работе/на приемке.
        foreach (var actionItemPart in _obj.ActionItemParts)
        {
          var actionItemPartTask = actionItemPart.ActionItemPartExecutionTask;
          if (actionItemPartTask.Status == Sungero.Workflow.Task.Status.InProcess)
          {
            var oldSupervisorAssignment = Functions.ActionItemExecutionTask.GetActualActionItemSupervisorAssignment(actionItemPartTask);
            
            this.UpdateActionItemTask(actionItemPartTask, changes);
            actionItemPartTask.OnEdit = true;
            actionItemPartTask.Save();
            
            try
            {
              // Переадресовать измененные задания.
              this.ForwardChangedAssignments(changes, null, oldSupervisorAssignment);
            }
            catch
            {
              actionItemPartTask.OnEdit = false;
              actionItemPartTask.Save();
              throw AppliedCodeException.Create(ActionItemExecutionTasks.Resources.ActionItemChangeError);
            }
            
            // Запустить корректировку в схеме, чтобы прекращение заблокированных заданий,
            // рестарт запроса продления срока и сброс признака выполнились там.
            actionItemPartTask.Blocks.Block110.Execute();
          }
        }
        
        // Прекратить неактуальные запросы отчетов от контролера.
        this.AbortReportRequestTasks(changes, null);
      }
      catch
      {
        _obj.OnEdit = false;
        _obj.Save();
        throw AppliedCodeException.Create(ActionItemExecutionTasks.Resources.ActionItemChangeError);
      }
      
      // TODO Если помимо контролера через действие на ленте будет меняться что-либо еще,
      // то необходимо будет здесь делать проверку, что контролер изменился,
      // и только тогда запускать данный блок.
      _obj.Blocks.Block110.Execute();
      
      // Разослать уведомления об изменении контролера.
      this.SendActionItemChangeNotifications(changes, addressees);
    }
    
    /// <summary>
    /// Проверить возможность изменения поручения перед показом диалога корректировки.
    /// </summary>
    /// <returns>Текст ошибки или пустую строку, если ошибок нет.</returns>
    [Remote(IsPure = true)]
    public virtual string CheckActionItemEditBeforeDialog()
    {
      // Проверить, что поручение никем не корректируется.
      var actionItemNotInChangingProcessErrorText = this.CheckActionItemNotInChangingProcess();
      if (!string.IsNullOrEmpty(actionItemNotInChangingProcessErrorText))
        return actionItemNotInChangingProcessErrorText;
      
      // Проверить, что поручение находится в работе.
      var actionItemInWorkErrorText = this.CheckActionItemInProcess();
      if (!string.IsNullOrEmpty(actionItemInWorkErrorText))
        return actionItemInWorkErrorText;
      
      // Проверить, что по поручению созданы все актуальные задания.
      var actionItemAssignmentsCreatedErrorText = this.CheckActionItemAssignmentsCreated();
      if (!string.IsNullOrEmpty(actionItemAssignmentsCreatedErrorText))
        return actionItemAssignmentsCreatedErrorText;
      
      // Проверить, что поручение никем не заблокировано.
      var actionItemNotLockedErrorText = this.CheckActionItemNotLocked();
      if (!string.IsNullOrEmpty(actionItemNotLockedErrorText))
        return actionItemNotLockedErrorText;
      
      return null;
    }
    
    /// <summary>
    /// Проверить возможность изменения пункта поручения.
    /// </summary>
    /// <param name="actionItemPartExecutionTask">Задача по пункту поручения.</param>
    /// <param name="dialogOpenDate">Дата открытия диалога.</param>
    /// <returns>Текст ошибки или пустую строку, если ошибок нет.</returns>
    [Remote(IsPure = true)]
    public virtual string CheckActionItemPartEdit(IActionItemExecutionTask actionItemPartExecutionTask,
                                                  DateTime? dialogOpenDate)
    {
      // Проверить, что пункт поручения уже создан.
      if (actionItemPartExecutionTask == null)
        return ActionItemExecutionTasks.Resources.ActionItemIsAlreadyInChangingProcess;
      
      // На приемке нельзя менять ответственного исполнителя в пункте поручения.
      if (Functions.ActionItemExecutionTask.CheckActionItemOnControl(actionItemPartExecutionTask))
        return ActionItemExecutionTasks.Resources.ActionItemOnControlCannotChangeAssignee;

      // Проверить, что главное поручение никем не корректируется.
      var mainTask = ActionItemExecutionTasks.As(actionItemPartExecutionTask.ParentTask);
      var mainActionItemNotChangedErrorText = Functions.ActionItemExecutionTask.CheckCurrentActionItemNotInChangingProcess(mainTask);
      if (!string.IsNullOrEmpty(mainActionItemNotChangedErrorText))
        return mainActionItemNotChangedErrorText;
      
      // Проверить, что пункт поручения никем не корректируется.
      var actionItemNotInChangingProcessErrorText = Functions.ActionItemExecutionTask.CheckCurrentActionItemNotInChangingProcess(actionItemPartExecutionTask);
      if (!string.IsNullOrEmpty(actionItemNotInChangingProcessErrorText))
        return actionItemNotInChangingProcessErrorText;
      
      // Проверить, что пункт поручения находится в работе.
      var actionItemInWorkErrorText = Functions.ActionItemExecutionTask.CheckActionItemInProcess(actionItemPartExecutionTask);
      if (!string.IsNullOrEmpty(actionItemInWorkErrorText))
        return actionItemInWorkErrorText;
      
      // Проверить, что по пункту поручения созданы все актуальные задания.
      var actionItemAssignmentsCreatedErrorText = Functions.ActionItemExecutionTask.CheckActionItemAssignmentsCreated(actionItemPartExecutionTask);
      if (!string.IsNullOrEmpty(actionItemAssignmentsCreatedErrorText))
        return actionItemAssignmentsCreatedErrorText;

      // Проверить, что головное поручение никем не заблокировано.
      var mainActionItemNotLockedErrorText = Functions.ActionItemExecutionTask.CheckActionItemNotLocked(mainTask);
      if (!string.IsNullOrEmpty(mainActionItemNotLockedErrorText))
        return mainActionItemNotLockedErrorText;
      
      // Проверить, что пункт поручения никем не заблокирован.
      var actionItemNotLockedErrorText = Functions.ActionItemExecutionTask.CheckActionItemNotLocked(actionItemPartExecutionTask);
      if (!string.IsNullOrEmpty(actionItemNotLockedErrorText))
        return actionItemNotLockedErrorText;
      
      // Проверить, что пункт поручения не был изменен, пока был открыт диалог.
      var actionItemChangedErrorText = this.CheckActionItemNotChanged(dialogOpenDate);
      if (!string.IsNullOrEmpty(actionItemChangedErrorText))
        return actionItemChangedErrorText;
      
      return null;
    }
    
    /// <summary>
    /// Проверить возможность изменения поручения в диалоге корректировки.
    /// </summary>
    /// <param name="assignee">Новый исполнитель.</param>
    /// <param name="dialogOpenDate">Дата открытия диалога.</param>
    /// <returns>Текст ошибки или пустую строку, если ошибок нет.</returns>
    /// <remarks>Параметр "Исполнитель" неактуален для главного составного поручения.</remarks>
    [Remote(IsPure = true)]
    public virtual string CheckActionItemEditInDialog(IEmployee assignee, DateTime? dialogOpenDate)
    {
      // На приемке нельзя менять ответственного исполнителя.
      if (_obj.IsCompoundActionItem != true && !Equals(_obj.Assignee, assignee) && this.CheckActionItemOnControl())
        return ActionItemExecutionTasks.Resources.ActionItemOnControlCannotChangeAssignee;
      
      // Проверить, что поручение никем не корректируется.
      var actionItemNotInChangingProcessErrorText = this.CheckActionItemNotInChangingProcess();
      if (!string.IsNullOrEmpty(actionItemNotInChangingProcessErrorText))
        return actionItemNotInChangingProcessErrorText;
      
      // Проверить, что поручение находится в работе.
      var actionItemInWorkErrorText = this.CheckActionItemInProcess();
      if (!string.IsNullOrEmpty(actionItemInWorkErrorText))
        return actionItemInWorkErrorText;
      
      // Проверить, что по поручению созданы все актуальные задания.
      var actionItemAssignmentsCreatedErrorText = this.CheckActionItemAssignmentsCreated();
      if (!string.IsNullOrEmpty(actionItemAssignmentsCreatedErrorText))
        return actionItemAssignmentsCreatedErrorText;
      
      // Проверить, что поручение никем не заблокировано.
      var actionItemNotLockedErrorText = this.CheckActionItemNotLocked();
      if (!string.IsNullOrEmpty(actionItemNotLockedErrorText))
        return actionItemNotLockedErrorText;
      
      // Проверить, что поручение не было изменено, пока был открыт диалог.
      var actionItemChangedErrorText = this.CheckActionItemNotChanged(dialogOpenDate);
      if (!string.IsNullOrEmpty(actionItemChangedErrorText))
        return actionItemChangedErrorText;
      
      return null;
    }
    
    /// <summary>
    /// Проверить, что поручение не корректируется в текущий момент.
    /// </summary>
    /// <returns>Текст ошибки, если корректируется. Иначе пустую строку.</returns>
    public virtual string CheckCurrentActionItemNotInChangingProcess()
    {
      var onEdit = ActionItemExecutionTasks.GetAll().Where(a => a.Id == _obj.Id && a.OnEdit == true).Any();
      if (onEdit)
        return ActionItemExecutionTasks.Resources.ActionItemIsAlreadyInChangingProcess;
      
      return null;
    }
    
    /// <summary>
    /// Проверить, что поручение (в том числе и пункты) не корректируется в текущий момент.
    /// </summary>
    /// <returns>Текст ошибки, если корректируется. Иначе пустую строку.</returns>
    public virtual string CheckActionItemNotInChangingProcess()
    {
      var anyActionItemPartOnEdit = _obj.ActionItemParts.Any(x => x.ActionItemPartExecutionTask != null && x.ActionItemPartExecutionTask.OnEdit.Value);
      var onEdit = ActionItemExecutionTasks.GetAll().Where(a => a.Id == _obj.Id && a.OnEdit == true).Any();
      if (onEdit || anyActionItemPartOnEdit)
        return ActionItemExecutionTasks.Resources.ActionItemIsAlreadyInChangingProcess;
      
      return null;
    }
    
    /// <summary>
    /// Проверить, что поручение находится в работе (не завершено и не прекращено).
    /// </summary>
    /// <returns>Текст ошибки, если завершено или прекращено. Иначе пустую строку.</returns>
    public virtual string CheckActionItemInProcess()
    {
      var actionItemInProcess = _obj.Status == Sungero.Workflow.Task.Status.InProcess;
      if (!actionItemInProcess)
        return ActionItemExecutionTasks.Resources.ActionItemExecuted;
      
      return null;
    }
    
    /// <summary>
    /// Проверить, что работы по поручению находятся на приемке.
    /// </summary>
    /// <returns>True - если работы на приемке, иначе - false.</returns>
    /// <remarks>Метод нужен для того, чтобы переполучить поручение и сравнить его актуальный статус.</remarks>
    public virtual bool CheckActionItemOnControl()
    {
      return _obj.ExecutionState == ExecutionState.OnControl;
    }
    
    /// <summary>
    /// Проверить, что у всех поручений есть как минимум одно стартованное задание.
    /// </summary>
    /// <param name="tasks">Поручения.</param>
    /// <returns>True, если у всех поручений есть задания. Иначе False.</returns>
    public virtual bool CheckAllAssignmentsOnTasksStarted(IQueryable<IActionItemExecutionTask> tasks)
    {
      foreach (var task in tasks)
      {
        if (!Assignments.GetAll().Where(a => Equals(a.Task, task)).Any())
          return false;
      }
      return true;
    }
    
    /// <summary>
    /// Проверить, что по поручению уже созданы все актуальные задания, и его можно корректировать.
    /// </summary>
    /// <returns>Текст ошибки, если задания не созданы. Иначе пустую строку.</returns>
    public virtual string CheckActionItemAssignmentsCreated()
    {
      var executionAssignment = this.GetActualActionItemExecutionAssignment();
      
      // В задачах соисполнителю должно быть хотя бы одно задание.
      var coAssigneeTasks = ActionItemExecutionTasks.GetAll()
        .Where(t => t.Status == Workflow.Task.Status.InProcess)
        .Where(t => t.ParentAssignment != null && Equals(t.ParentAssignment.Task, _obj))
        .Where(t => t.ActionItemType == ActionItemType.Additional);
      var allAssignmentsStarted = this.CheckAllAssignmentsOnTasksStarted(coAssigneeTasks);
      
      // В пунктах составного поручения должно быть хотя бы одно задание.
      var compoundActionItemTasks = ActionItemExecutionTasks.GetAll()
        .Where(t => t.Status == Workflow.Task.Status.InProcess)
        .Where(t => Equals(t.ParentTask, _obj))
        .Where(t => t.ActionItemType == ActionItemType.Component);
      var allCompoundAssignmentsStarted = this.CheckAllAssignmentsOnTasksStarted(compoundActionItemTasks);
      
      var coAssigneesAssignmentNotCreated = _obj.CoAssignees.Any(ca => ca.AssignmentCreated != true) ||
        !allAssignmentsStarted;
      var actionItemPartsAssignmentNotCreated = _obj.ActionItemParts.Any(aip => aip.AssignmentCreated != true) ||
        !allCompoundAssignmentsStarted;
      var actionItemOnExecution = _obj.ExecutionState == ExecutionState.OnExecution || _obj.ExecutionState == ExecutionState.OnRework;
      
      // Проверить (описанные ниже ситуации могут случаться в каких-то сочетаниях), что:
      // - по простому поручению не созданы подзадачи соисполнителям;
      // - по составному поручению не созданы подзадачи по пунктам;
      // - поручение на исполнении и нет задания на исполнение в работе.
      if (coAssigneesAssignmentNotCreated ||
          actionItemPartsAssignmentNotCreated ||
          actionItemOnExecution && executionAssignment == null)
        return ActionItemExecutionTasks.Resources.ActionItemIsAlreadyInChangingProcess;
      
      return null;
    }

    /// <summary>
    /// Проверить, что карточка поручения не заблокирована другими пользователями.
    /// </summary>
    /// <returns>Текст ошибки, если заблокирована. Иначе пустую строку.</returns>
    public virtual string CheckActionItemNotLocked()
    {
      var taskLockInfo = Locks.GetLockInfo(_obj);
      if (taskLockInfo.IsLockedByOther)
        return ActionItemExecutionTasks.Resources.ActionItemExecutionTaskLockedByUserFormat(taskLockInfo.OwnerName);
      
      return null;
    }
    
    /// <summary>
    /// Проверить, что поручение не было изменено с момента указанной даты.
    /// </summary>
    /// <param name="lastActionItemChangeDate">Дата последнего изменения поручения.</param>
    /// <returns>Текст ошибки, если поручение было изменено. Иначе пустую строку.</returns>
    public virtual string CheckActionItemNotChanged(DateTime? lastActionItemChangeDate)
    {
      if (!lastActionItemChangeDate.HasValue)
        return null;
      
      var actualLastActionItemChangeDate = this.GetLastActionItemChangeDate();
      if (lastActionItemChangeDate < actualLastActionItemChangeDate)
        return ActionItemExecutionTasks.Resources.ActionItemWasChanged;
      
      return null;
    }
    
    /// <summary>
    /// Получить дату последнего изменения поручения.
    /// </summary>
    /// <returns>Дата последнего изменения поручения.</returns>
    public virtual DateTime? GetLastActionItemChangeDate()
    {
      return _obj.History.GetAll()
        .Where(x => x.Action == Sungero.CoreEntities.History.Action.Update)
        .Select(x => x.HistoryDate)
        .OrderByDescending(x => x)
        .FirstOrDefault();
    }
    
    /// <summary>
    /// Изменить свойства задачи на исполнение поручения.
    /// </summary>
    /// <param name="task">Поручение.</param>
    /// <param name="changes">Изменения в поручении.</param>
    public virtual void UpdateActionItemTask(IActionItemExecutionTask task, IActionItemChanges changes)
    {
      // Изменение контролёра и постановка на контроль.
      if (changes.NewSupervisor != null)
      {
        // Галочка "На контроле" сбрасывает значение контролера,
        // поэтому ее надо установить перед изменением этого поля.
        task.IsUnderControl = true;
        task.Supervisor = changes.NewSupervisor;
      }
      
      // Изменение основного исполнителя.
      if (changes.NewAssignee != null)
        task.Assignee = changes.NewAssignee;
      
      // Удаление соисполнителей.
      int assigneeNumber = 0;
      while (assigneeNumber < task.CoAssignees.Count())
      {
        var assignee = task.CoAssignees.ElementAt(assigneeNumber);
        if (!changes.NewCoAssignees.Contains(assignee.Assignee))
          task.CoAssignees.Remove(assignee);
        else
          assigneeNumber++;
      }
      
      // Добавление соисполнителей.
      if (changes.NewCoAssignees != null)
      {
        foreach (var assignee in changes.NewCoAssignees.Except(changes.OldCoAssignees))
        {
          var newAssignee = task.CoAssignees.AddNew();
          newAssignee.Assignee = assignee;
        }
      }
    }
    
    /// <summary>
    /// Прекратить подзадачи по удаленным соисполнителям.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    /// <remarks>Прекращаются поручения, запросы отчетов, запросы продления сроков.</remarks>
    public virtual void AbortDeletedCoAssigneeTasks(IActionItemChanges changes)
    {
      foreach (var assignee in changes.OldCoAssignees.Except(changes.NewCoAssignees))
      {
        var task = ActionItemExecutionTasks.GetAll()
          .Where(t => t.ParentAssignment != null && Equals(_obj, t.ParentAssignment.Task) && Equals(assignee, t.Assignee))
          .Where(t => t.Status == RecordManagement.ActionItemExecutionTask.Status.InProcess)
          .Where(t => t.ActionItemType == ActionItemType.Additional)
          .FirstOrDefault();
        
        if (task != null)
        {
          this.AbortDeadlineExtensionTasks(task);
          this.AbortReportRequestTasksToOldCoAssignee(task);
          task.Abort();
        }
      }
    }
    
    /// <summary>
    /// Прекратить подзадачи на запрос продления срока по удаленным соисполнителям.
    /// </summary>
    /// <param name="task">Поручение соисполнителю.</param>
    public virtual void AbortDeadlineExtensionTasks(IActionItemExecutionTask task)
    {
      var assignments = ActionItemExecutionAssignments.GetAll()
        .Where(j => Equals(j.Task, task))
        .Where(j => j.TaskStartId == _obj.StartId);
      
      foreach (var assignment in assignments)
      {
        var extendDeadlineTasks = Docflow.DeadlineExtensionTasks.GetAll()
          .Where(t => Equals(t.ParentAssignment, assignment) &&
                 t.Status == Workflow.Task.Status.InProcess);
        foreach (var extendDeadlineTask in extendDeadlineTasks)
          extendDeadlineTask.Abort();
      }
    }
    
    /// <summary>
    /// Прекратить подзадачи на запрос отчета по поручению удаленному соисполнителю.
    /// </summary>
    /// <param name="task">Поручение соисполнителю.</param>
    public virtual void AbortReportRequestTasksToOldCoAssignee(IActionItemExecutionTask task)
    {
      var assignment = this.GetActionItemExecutionAssignment();
      var manualSubtasksExist = ActionItemExecutionTasks.GetAll()
        .Where(t => Equals(t.ParentAssignment, assignment) && Equals(t.Assignee, task.Assignee) &&
               !Equals(t, task) && t.Status == Workflow.Task.Status.InProcess).Any();
      
      // Запрос отчета из поручения соисполнителю прекращаем всегда.
      // Запрос отчета из задания исполнителя прекращаем,
      // только если не было ручных подзадач тому же соисполнителю.
      var reportRequestTasks = StatusReportRequestTasks.GetAll()
        .Where(t => (Equals(t.ParentTask, task) ||
                     Equals(t.ParentAssignment, assignment) && !manualSubtasksExist) &&
               Equals(t.Assignee, task.Assignee) &&
               t.Status == Workflow.Task.Status.InProcess);
      foreach (var reportRequestTask in reportRequestTasks)
        reportRequestTask.Abort();
    }
    
    /// <summary>
    /// Разослать уведомления об изменении поручения.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    /// <param name="addressees">Адресаты.</param>
    public virtual void SendActionItemChangeNotifications(IActionItemChanges changes, List<IUser> addressees)
    {
      if (!addressees.Any())
        return;
      
      var noticeSubject = Functions.ActionItemExecutionTask.GetActionItemExecutionSubject(_obj, ActionItemExecutionTasks.Resources.ActionItemChanged);
      var activeText = this.GetActionItemChangeNotificationText(changes);
      Docflow.PublicFunctions.Module.Remote.SendNoticesAsSubtask(noticeSubject,
                                                                 addressees,
                                                                 _obj,
                                                                 activeText,
                                                                 Users.Current,
                                                                 ActionItemExecutionTasks.Resources.ActionItemExecutionChangeNotification);
    }
    
    /// <summary>
    /// Получить список заинтересованных в изменении поручения.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    /// <param name="oldExecutionAssignmentInProcess">Признак того, что старое задание на исполнение поручения находится в работе.</param>
    /// <returns>Список пользователей, кого необходимо уведомить.</returns>
    public virtual List<IUser> GetActionItemChangeNotificationAddressees(IActionItemChanges changes,
                                                                         bool oldExecutionAssignmentInProcess)
    {
      var addressees = new List<IUser>();

      var assigneeChanged = !Equals(changes.OldAssignee, changes.NewAssignee);
      var supervisorChanged = !Equals(changes.OldSupervisor, changes.NewSupervisor);
      var coAssigneesChanged = !changes.OldCoAssignees.SequenceEqual(changes.NewCoAssignees);
      
      // Изменение исполнителя.
      if (assigneeChanged && oldExecutionAssignmentInProcess)
      {
        addressees.Add(changes.OldAssignee);
        addressees.Add(changes.NewSupervisor);
        foreach (var coAssignee in changes.NewCoAssignees)
        {
          // Отправляем только тем соисполнителям, у которых задание на исполнении или на доработке.
          var coAssigneeAssignmentInWork = ActionItemExecutionTasks.GetAll()
            .Where(t => t.ParentAssignment != null && Equals(_obj, t.ParentAssignment.Task) && Equals(coAssignee, t.Assignee))
            .Where(t => t.ExecutionState == RecordManagement.ActionItemExecutionTask.ExecutionState.OnExecution ||
                   t.ExecutionState == RecordManagement.ActionItemExecutionTask.ExecutionState.OnRework)
            .Any();
          if (coAssigneeAssignmentInWork)
            addressees.Add(coAssignee);
        }
      }
      
      // Изменение контролера.
      if (supervisorChanged)
      {
        addressees.Add(changes.OldSupervisor);
        if (oldExecutionAssignmentInProcess && changes.NewSupervisor != null)
          addressees.Add(changes.NewSupervisor);
        
        // Уведомить ответственного исполнителя, если он не менялся и еще не выполнил задание.
        if (!assigneeChanged && oldExecutionAssignmentInProcess)
          addressees.Add(changes.NewAssignee);
      }
      
      // Изменение соисполнителей.
      if (coAssigneesChanged)
      {
        // Уведомить ответственного исполнителя, если самому исполнителю не будет нового задания.
        if (!assigneeChanged)
          addressees.Add(changes.NewAssignee);
        
        // Уведомить удаленных соисполнителей (вновь добавленных не надо, так как им придет подзадача).
        var deletedCoAssignees = changes.OldCoAssignees.Except(changes.NewCoAssignees);
        foreach (var assignee in deletedCoAssignees)
          addressees.Add(assignee);
      }
      
      // Добавить автора на случай, если поручение меняет не он.
      // Если текущее простое поручение - это пункт составного, то автора берем из основной задачи,
      // т.к. в пункт поручения инициатор основной задачи не прописывается.
      var mainTask = _obj.ActionItemType == ActionItemType.Component
        ? ActionItemExecutionTasks.As(_obj.ParentTask)
        : _obj;
      
      var author = mainTask.StartedBy.IsSystem != true ? mainTask.StartedBy : mainTask.AssignedBy;
      if (!Equals(Users.Current, author))
        addressees.Add(author);
      
      // Устранить дублирование адресатов, в том числе убрать себя.
      addressees = addressees.Distinct().Where(a => a != null && a.IsSystem != true).ToList();
      if (addressees.Contains(Employees.Current))
        addressees.Remove(Employees.Current);
      
      return addressees;
    }
    
    /// <summary>
    /// Получить список заинтересованных в изменении составного поручения.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    /// <returns>Список пользователей, кого необходимо уведомить.</returns>
    public virtual List<IUser> GetCompoundActionItemChangeNotificationAddressees(IActionItemChanges changes)
    {
      var addressees = new List<IUser>();
      
      // Уведомление старому контролеру.
      addressees.Add(changes.OldSupervisor);
      
      // Уведомление новому контролеру.
      addressees.Add(changes.NewSupervisor);
      
      // Уведомления исполнителям пунктов поручения, которые не выполнили еще свое задание.
      var actionItemPartExecutionTasksIds = _obj.ActionItemParts.Select(x => x.ActionItemPartExecutionTask.Id);
      var actionItemPartExecutionAssignments = ActionItemExecutionAssignments.GetAll().Where(x => actionItemPartExecutionTasksIds.Contains(x.Task.Id));
      var assignmentsPerformers = actionItemPartExecutionAssignments.Where(x => x.Status == RecordManagement.ActionItemExecutionAssignment.Status.InProcess)
        .Select(x => x.Performer);
      addressees.AddRange(assignmentsPerformers);
      
      // Добавить автора на случай, если поручение меняет не он.
      var author = _obj.StartedBy.IsSystem != true ? _obj.StartedBy : _obj.AssignedBy;
      if (!Equals(Users.Current, author))
        addressees.Add(author);
      
      // Устранить дублирование адресатов, в том числе убрать себя.
      addressees = addressees.Distinct().Where(a => a != null).ToList();
      if (addressees.Contains(Employees.Current))
        addressees.Remove(Employees.Current);
      
      return addressees;
    }
    
    /// <summary>
    /// Получить текст уведомления об изменении поручения.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    /// <returns>Текст изменений в поручении.</returns>
    public virtual string GetActionItemChangeNotificationText(IActionItemChanges changes)
    {
      var text = new List<string>();
      
      var assigneeChanged = !Equals(changes.OldAssignee, changes.NewAssignee);
      var supervisorChanged = !Equals(changes.OldSupervisor, changes.NewSupervisor);
      var coAssigneesChanged = changes.OldCoAssignees != null && !changes.OldCoAssignees.SequenceEqual(changes.NewCoAssignees);
      // Удалённые соисполнители.
      var deletedCoAssignees = changes.OldCoAssignees.Except(changes.NewCoAssignees);
      // Добавленные соисполнители.
      var addedCoAssignees = changes.NewCoAssignees.Except(changes.OldCoAssignees);
      
      // Изменение контролера.
      if (supervisorChanged)
      {
        if (changes.OldSupervisor != null && changes.NewSupervisor != null)
        {
          var oldSupervisorHyperlink = Hyperlinks.Get(changes.OldSupervisor);
          var newSupervisorHyperlink = Hyperlinks.Get(changes.NewSupervisor);
          text.Add(ActionItemExecutionTasks.Resources.SupervisorChangedFromToFormat(oldSupervisorHyperlink, newSupervisorHyperlink));
        }
        
        // Постановка на контроль.
        if (changes.OldSupervisor == null && changes.NewSupervisor != null)
        {
          var newSupervisorHyperlink = Hyperlinks.Get(changes.NewSupervisor);
          text.Add(ActionItemExecutionTasks.Resources.TaskPutUnderSupervisionFormat(newSupervisorHyperlink));
        }
      }
      
      // Изменение исполнителя.
      if (assigneeChanged)
      {
        var oldAssigneeHyperlink = Hyperlinks.Get(changes.OldAssignee);
        var newAssigneeHyperlink = Hyperlinks.Get(changes.NewAssignee);
        text.Add(ActionItemExecutionTasks.Resources.AssigneeChangedFromToFormat(oldAssigneeHyperlink, newAssigneeHyperlink));
      }
      
      // Изменение соисполнителей.
      if (coAssigneesChanged)
      {
        if (deletedCoAssignees.Any())
        {
          var deletedCoAssigneesHyperlink = deletedCoAssignees
            .Select(a => Hyperlinks.Get(a));
          
          text.Add(ActionItemExecutionTasks.Resources.CoAssigneesDeletedFormat(string.Join("; ", deletedCoAssigneesHyperlink)));
        }
        
        if (addedCoAssignees.Any())
        {
          var addedCoAssigneesHyperlink = addedCoAssignees
            .Select(a => Hyperlinks.Get(a));
          text.Add(ActionItemExecutionTasks.Resources.CoAssigneesAddedFormat(string.Join("; ", addedCoAssigneesHyperlink)));
        }
      }
      
      if (assigneeChanged || deletedCoAssignees.Any())
        text.Add(Sungero.RecordManagement.ActionItemExecutionTasks.Resources.StopAssignmentSubtask);
      
      return string.Join(Environment.NewLine, text);
    }
    
    /// <summary>
    /// Переадресовать подчиненные задания, у которых изменился исполнитель или контролер.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    /// <param name="oldExecutionAssignment">Старое задание на исполнение поручения.</param>
    /// <param name="oldSupervisorAssignment">Старое задание на контроль исполнения.</param>
    public virtual void ForwardChangedAssignments(IActionItemChanges changes,
                                                  IAssignment oldExecutionAssignment,
                                                  IAssignment oldSupervisorAssignment)
    {
      if (oldExecutionAssignment != null && changes.NewAssignee != null && !Equals(oldExecutionAssignment.Performer, changes.NewAssignee))
        this.ForwardAssignment(oldExecutionAssignment, changes.NewAssignee);
      
      if (oldSupervisorAssignment != null && changes.NewSupervisor != null && !Equals(oldSupervisorAssignment.Performer, changes.NewSupervisor))
        this.ForwardAssignment(oldSupervisorAssignment, changes.NewSupervisor);
    }
    
    /// <summary>
    /// Переадресовать задание новому исполнителю и попытаться прекратить задание старому.
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <param name="performer">Новый исполнитель.</param>
    /// <remarks>Если "старое задание" заблокировано, то будет выполнена только переадресация,
    /// а прекращение будет в рамках схемы подзадачи соисполнителю.</remarks>
    public virtual void ForwardAssignment(IAssignment assignment, IUser performer)
    {
      assignment.Forward(performer);
      if (!Locks.GetLockInfo(assignment).IsLocked)
        assignment.Abort();
    }
    
    /// <summary>
    /// Скорректировать дерево поручений соисполнителям в соответствии с новым ответственным исполнителем.
    /// </summary>
    public virtual void ChangeCoAssigneesActionItemsTree()
    {
      var newExecutionAssignment = this.GetActionItemExecutionAssignment();
      var wasExecutionAssignmentForwarded = newExecutionAssignment != null && newExecutionAssignment.ForwardedFrom != null;
      if (!wasExecutionAssignmentForwarded)
        return;
      
      var oldCoAssigneeTasks = this.GetCoAssigneeActionItemExecutionTasks(newExecutionAssignment.ForwardedFrom);
      
      foreach (var task in oldCoAssigneeTasks)
      {
        // Сменить контролера в поручении-подзадаче соисполнителю и перецепить поручение-подзадачу к заданию актуальному исполнителю.
        var supervisorAssignment = Functions.ActionItemExecutionTask.GetActualActionItemSupervisorAssignment(task);
        Functions.ActionItemExecutionTask.ChangeCoAssigneeActionItemExecutionTaskSupervisor(task, newExecutionAssignment, _obj.Assignee);
        
        // Установить режим корректировки и сохранить.
        task.OnEdit = true;
        task.Save();
        
        // Рестартовать запросы продления срока для подзадачи соисполнителю.
        Functions.ActionItemExecutionTask.RestartDeadlineExtensionTasks(task, _obj.Assignee);
        
        // Если в подзадаче соисполнителю есть задание на приемку, то его надо переадресовать.
        if (supervisorAssignment != null)
          this.ForwardAssignment(supervisorAssignment, task.Supervisor);
        
        // Запустить схему для отработки следующих ситуаций:
        // - чтобы выдать права контролеру при его изменении, если поручение в этот момент находилось на исполнении;
        // - чтобы прекратить старые задания, которые были заблочены при переадресации и не смогли вовремя прекратиться.
        task.Blocks.Block110.Execute();
      }
    }
    
    /// <summary>
    /// Сменить контролера в поручении соисполнителю и перецепить поручение к новому родительскому заданию.
    /// </summary>
    /// <param name="newParentAssignment">Новое родительское задание.</param>
    /// <param name="newSupervisor">Новый контролер.</param>
    public virtual void ChangeCoAssigneeActionItemExecutionTaskSupervisor(IAssignment newParentAssignment, IEmployee newSupervisor)
    {
      ((IInternalTask)_obj).ParentAssignment = newParentAssignment;
      if (_obj.Status == Sungero.Workflow.Task.Status.InProcess)
      {
        _obj.Supervisor = newSupervisor;
        _obj.AssignedBy = newSupervisor;
      }
    }
    
    /// <summary>
    /// Прекратить задание старому исполнителю (при переадресации).
    /// </summary>
    public virtual void AbortActionItemExecutionAssignment()
    {
      var newExecutionAssignment = this.GetActionItemExecutionAssignment();
      var wasExecutionAssignmentForwarded = newExecutionAssignment != null && newExecutionAssignment.ForwardedFrom != null;
      if (wasExecutionAssignmentForwarded &&
          newExecutionAssignment.ForwardedFrom.Status == Sungero.Workflow.Assignment.Status.InProcess)
        newExecutionAssignment.ForwardedFrom.Abort();
    }
    
    /// <summary>
    /// Прекратить задание старому контролеру (при переадресации).
    /// </summary>
    /// <remarks>Для ручных поручений такое задание будет всегда максимум одно
    /// (так как корректировать поручение до прекращения задания контролеру
    /// и последующего сброса в false признака OnEdit нельзя).
    /// А вот для автоматических поручений соисполнителям из-за того,
    /// что в головной задаче могут скорректировать отв. исполнителя
    /// (который одновременно является и контролером в подпоручениях соисполнителей),
    /// может возникнуть ситуация частой смены контролера, а значит, переадресации заданий на приемку.
    /// Если при этом какое-либо прекращаемое задание будет заблокировано на момент переадресации,
    /// то при следующей переадресации будет уже 2 или более "старых" заданий, которые нужно прекращать.</remarks>
    public virtual void AbortActionItemSupervisorAssignments()
    {
      var assignmentsInProcess = this.GetActionItemSupervisorAssignmentsInProcess();
      if (assignmentsInProcess.Any())
      {
        var actualSupervisorAssignment = this.GetActualActionItemSupervisorAssignment();
        if (actualSupervisorAssignment != null)
          assignmentsInProcess.Remove(actualSupervisorAssignment);
        
        foreach (var assignment in assignmentsInProcess)
          assignment.Abort();
      }
    }
    
    /// <summary>
    /// Прекратить неактуальные запросы отчетов.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    /// <param name="oldExecutionAssignment">Старое задание на исполнение поручения.</param>
    public virtual void AbortReportRequestTasks(IActionItemChanges changes,
                                                IAssignment oldExecutionAssignment)
    {
      if (oldExecutionAssignment != null && changes.NewAssignee != null && !Equals(oldExecutionAssignment.Performer, changes.NewAssignee))
      {
        // Если это простое поручение, прекратить запросы отчета от старого исполнителя.
        // Запросы отчета к исполнителю, направленные непосредственно из задачи, прекратятся в схеме, блок 4 "Выполнение задания".
        this.AbortReportRequestTasksFromOldAssignee(ActionItemExecutionAssignments.As(oldExecutionAssignment), changes);
        
        // Если это подчиненное простое поручение, прекратить запросы отчета к старому исполнителю из родительского задания.
        if (_obj.ParentAssignment != null && ActionItemExecutionAssignments.Is(_obj.ParentAssignment))
          this.AbortReportRequestTasksToSubActionItemAssignee(changes);
      }
      
      // Если это пункт составного поручения, прекратить запросы отчета к старому исполнителю.
      if (oldExecutionAssignment != null && _obj.ActionItemType == ActionItemType.Component && !Equals(oldExecutionAssignment.Performer, changes.NewAssignee))
        this.AbortReportRequestTasksToOldActionItemPartAssignee(changes);
      
      // Прекратить запросы отчета от старого контролера.
      if (!Equals(changes.OldSupervisor, changes.NewSupervisor))
        this.AbortReportRequestTasksFromOldSupervisor(changes.OldSupervisor);
    }
    
    /// <summary>
    /// Прекратить запросы отчета по поручению от старого ответственного исполнителя.
    /// </summary>
    /// <param name="oldExecutionAssignment">Старое задание ответственному исполнителю.</param>
    /// <param name="changes">Изменения в поручении.</param>
    public virtual void AbortReportRequestTasksFromOldAssignee(IActionItemExecutionAssignment oldExecutionAssignment,
                                                               IActionItemChanges changes)
    {
      // Прекратить все запросы отчета, созданные ответственным исполнителем из задания.
      var reportRequestTasks = StatusReportRequestTasks.GetAll()
        .Where(t => Equals(t.ParentAssignment, oldExecutionAssignment) && Equals(t.Author, changes.OldAssignee) &&
               t.Status == Workflow.Task.Status.InProcess);
      foreach (var reportRequestTask in reportRequestTasks)
        reportRequestTask.Abort();
      
      // Прекратить все запросы отчета, созданные ответственным исполнителем из подзадач соисполнителям.
      foreach (var coAssignee in changes.OldCoAssignees)
      {
        var task = ActionItemExecutionTasks.GetAll()
          .Where(t => Equals(t.ParentAssignment, oldExecutionAssignment) && Equals(t.Assignee, coAssignee) && Equals(t.Author, changes.OldAssignee))
          .Where(t => t.Status == RecordManagement.ActionItemExecutionTask.Status.InProcess)
          .Where(t => t.ActionItemType == ActionItemType.Additional)
          .FirstOrDefault();
        
        if (task != null)
        {
          var reportRequestTasksFromSubtask = StatusReportRequestTasks.GetAll()
            .Where(t => Equals(t.ParentTask, task) &&
                   Equals(t.Assignee, task.Assignee) &&
                   t.Status == Workflow.Task.Status.InProcess);
          foreach (var reportRequestTask in reportRequestTasksFromSubtask)
            reportRequestTask.Abort();
        }
      }
    }
    
    /// <summary>
    /// Прекратить запросы отчета, направленные старому исполнителю составного поручения.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    public virtual void AbortReportRequestTasksToOldActionItemPartAssignee(IActionItemChanges changes)
    {
      var mainTask = ActionItemExecutionTasks.As(_obj.ParentTask);
      if (mainTask == null || mainTask.IsCompoundActionItem != true)
        return;
      // Если старого исполнителя больше нет среди исполнителей составного поручения,
      // то прекратить все запросы отчета к нему, созданные из задачи.
      var remainingAssignees = mainTask.ActionItemParts.Where(p => !Equals(p.ActionItemPartExecutionTask, _obj)).Select(p => p.Assignee).Distinct();
      if (remainingAssignees.Contains(changes.OldAssignee))
        return;
      
      var reportRequestTasks = StatusReportRequestTasks.GetAll()
        .Where(t => Equals(t.ParentTask, mainTask) && Equals(t.Assignee, changes.OldAssignee) &&
               t.Status == Workflow.Task.Status.InProcess);
      foreach (var reportRequestTask in reportRequestTasks)
        reportRequestTask.Abort();
    }
    
    /// <summary>
    /// Прекратить запросы отчета, направленные старому исполнителю простого подчиненного поручения.
    /// </summary>
    /// <param name="changes">Изменения в поручении.</param>
    public virtual void AbortReportRequestTasksToSubActionItemAssignee(IActionItemChanges changes)
    {
      var parentAssignment = ActionItemExecutionAssignments.As(_obj.ParentAssignment);
      if (parentAssignment == null)
        return;
      
      // Убедиться, что старому исполнителю нет других подчиненных поручений. Если есть - запросы не прекращаем.
      var otherSubtasksExist = ActionItemExecutionTasks.GetAll()
        .Where(t => Equals(t.ParentAssignment, parentAssignment) && Equals(t.Assignee, changes.OldAssignee) &&
               !Equals(t, _obj) && t.Status == Workflow.Task.Status.InProcess).Any();
      
      if (otherSubtasksExist)
        return;
      
      // Прекратить все запросы отчета к старому исполнителю, созданные из родительского задания.
      var reportRequestTasks = StatusReportRequestTasks.GetAll()
        .Where(t => Equals(t.ParentAssignment, parentAssignment) && Equals(t.Assignee, changes.OldAssignee) &&
               t.Status == Workflow.Task.Status.InProcess);
      foreach (var reportRequestTask in reportRequestTasks)
        reportRequestTask.Abort();
    }
    
    /// <summary>
    /// Прекратить запросы отчета по поручению от старого контролера.
    /// </summary>
    /// <param name="oldSupervisor">Старый контролер.</param>
    public virtual void AbortReportRequestTasksFromOldSupervisor(IEmployee oldSupervisor)
    {
      
      // Прекратить все запросы отчета, созданные старым контролером из головной задачи.
      var reportRequestTasks = StatusReportRequestTasks.GetAll()
        .Where(t => Equals(t.ParentTask, _obj) &&
               Equals(t.Author, oldSupervisor) &&
               t.Status == Workflow.Task.Status.InProcess);
      foreach (var reportRequestTask in reportRequestTasks)
        reportRequestTask.Abort();
      
      if (_obj.IsCompoundActionItem != true)
        return;
      
      // Прекратить все запросы отчета, созданные старым контролером из пункта составного поручения.
      var activePartTasks = ActionItemExecutionTasks.GetAll().Where(p => p.Status == RecordManagement.ActionItemExecutionTask.Status.InProcess &&
                                                                    p.ActionItemType == ActionItemType.Component &&
                                                                    p.ParentTask == _obj);
      
      foreach (var partTask in activePartTasks)
      {
        var reportRequestTasksFromSubtask = StatusReportRequestTasks.GetAll()
          .Where(t => Equals(t.ParentTask, partTask) &&
                 Equals(t.Author, oldSupervisor) &&
                 t.Status == Workflow.Task.Status.InProcess);
        foreach (var reportRequestTask in reportRequestTasksFromSubtask)
          reportRequestTask.Abort();
      }
    }
    
    /// <summary>
    /// Рестартовать подзадачи на запрос продления срока.
    /// </summary>
    /// <param name="actualSupervisor">Актуальный контролер, у которого запрашиваем новое продление.</param>
    public virtual void RestartDeadlineExtensionTasks(IEmployee actualSupervisor)
    {
      // Ищем задания, из которых запрашивали продление.
      var assignments = ActionItemExecutionAssignments.GetAll()
        .Where(j => Equals(j.Task, _obj))
        .Where(j => j.TaskStartId == _obj.StartId)
        .Where(j => j.Performer == _obj.Assignee);
      
      foreach (var assignment in assignments)
      {
        var deadlineExtensionTasks = Docflow.DeadlineExtensionTasks.GetAll()
          .Where(t => Equals(t.ParentAssignment, assignment) &&
                 t.Status == Workflow.Task.Status.InProcess);
        foreach (var deadlineExtensionTask in deadlineExtensionTasks)
        {
          if (deadlineExtensionTask.Assignee == actualSupervisor)
            continue;
          deadlineExtensionTask.Abort();
          var newDeadlineExtensionTask = Docflow.DeadlineExtensionTasks.CreateAsSubtask(assignment);
          newDeadlineExtensionTask.Assignee = actualSupervisor;
          newDeadlineExtensionTask.NewDeadline = deadlineExtensionTask.NewDeadline;
          newDeadlineExtensionTask.ActiveText = deadlineExtensionTask.ActiveText;
          newDeadlineExtensionTask.Author = deadlineExtensionTask.Author;
          newDeadlineExtensionTask.Subject = deadlineExtensionTask.Subject;
          newDeadlineExtensionTask.Save();
          Workflow.SpecialFolders.GetOutbox(newDeadlineExtensionTask.Author).Items.Add(newDeadlineExtensionTask);
          newDeadlineExtensionTask.Start();
        }
      }
    }
    
    #endregion
    
    /// <summary>
    /// Установить статусы в документе из поручения.
    /// </summary>
    public virtual void SetDocumentStates()
    {
      var document = _obj.DocumentsGroup.OfficialDocuments.FirstOrDefault();
      if (document == null || !document.AccessRights.CanUpdate())
        return;

      var tasksWithDocument = Sungero.Docflow.PublicFunctions.OfficialDocument.Remote.GetCreatedActionItems(document).ToList();
      var firstLevelTasks = Sungero.Docflow.PublicFunctions.OfficialDocument.GetFirstLevelActionItems(document);

      Enumeration? executionState = Docflow.OfficialDocument.ExecutionState.WithoutExecut;
      Enumeration? controlExecutionState = Docflow.OfficialDocument.ControlExecutionState.WithoutControl;
      
      var inProcess = firstLevelTasks.Where(t => t.ExecutionState == ExecutionState.OnExecution ||
                                            t.ExecutionState == ExecutionState.OnRework ||
                                            t.ExecutionState == ExecutionState.OnControl)
        .ToList();
      
      // Добавить составные поручения, если хотя бы один пункт поручения в процессе исполнения.
      var compoundTasks = firstLevelTasks.Where(i => i.IsCompoundActionItem.Value == true);
      inProcess.AddRange(compoundTasks.Where(t => t.ActionItemParts.Any(i => i.ActionItemPartExecutionTask == null ||
                                                                        i.ActionItemPartExecutionTask.ExecutionState == ExecutionState.OnExecution ||
                                                                        i.ActionItemPartExecutionTask.ExecutionState == ExecutionState.OnRework ||
                                                                        i.ActionItemPartExecutionTask.ExecutionState == ExecutionState.OnControl)));
      
      if (inProcess.Any())
      {
        executionState = Docflow.OfficialDocument.ExecutionState.OnExecution;

        if (inProcess.Any(t => t.IsUnderControl == true))
        {
          controlExecutionState = inProcess.Any(t => t.IsUnderControl == true &&
                                                t.Importance == Sungero.RecordManagement.ActionItemExecutionTask.Importance.High)
            ? Docflow.OfficialDocument.ControlExecutionState.SpecialControl
            : Docflow.OfficialDocument.ControlExecutionState.OnControl;
        }
      }
      else
      {
        var executeTasks = firstLevelTasks.Where(t => t.ExecutionState == ExecutionState.Executed).ToList();
        /* compoundTasks.Cast<ITask>() нужен для того, чтобы не возникало ошибок в приведении типов
         * между t.ParentTask (ITask) и compoundTasks (IActionItemExecutionTask).
         */
        executeTasks.AddRange(tasksWithDocument.Where(t => compoundTasks.Cast<ITask>().Contains(t.ParentTask) &&
                                                      t.ExecutionState == ExecutionState.Executed));
        
        if (executeTasks.Any())
        {
          executionState = Docflow.OfficialDocument.ExecutionState.Executed;
          if (executeTasks.Any(t => t.IsUnderControl == true))
            controlExecutionState = Docflow.OfficialDocument.ControlExecutionState.ControlRemoved;
        }
      }
      
      if (firstLevelTasks.All(t => t.ExecutionState == ExecutionState.Aborted) && firstLevelTasks.Count > 0)
      {
        executionState = Docflow.OfficialDocument.ExecutionState.Aborted;
        controlExecutionState = null;
      }
      
      Functions.Module.SetDocumentExecutionState(_obj, document, executionState);
      
      if (document.ExecutionState != ExecutionState.OnExecution &&
          document.ExecutionState != ExecutionState.Executed)
        controlExecutionState = null;
      
      Sungero.Docflow.PublicFunctions.OfficialDocument.SetControlExecutionState(document, controlExecutionState);
    }
    
    /// <summary>
    /// Получить незавершенные подчиненные поручения.
    /// </summary>
    /// <param name="entity"> Поручение, для которого требуется получить незавершенные.</param>
    /// <returns>Список незавершенных подчиненных поручений.</returns>
    [Remote(IsPure = true)]
    public static List<IActionItemExecutionTask> GetSubActionItemExecutions(Sungero.RecordManagement.IActionItemExecutionAssignment entity)
    {
      return ActionItemExecutionTasks.GetAll()
        .Where(t => entity != null && t.ParentAssignment == entity)
        .Where(t => t.ActionItemType == RecordManagement.ActionItemExecutionTask.ActionItemType.Additional ||
               t.ActionItemType == RecordManagement.ActionItemExecutionTask.ActionItemType.Main)
        .Where(t => t.Status.Value == Workflow.Task.Status.InProcess)
        .ToList();
    }
    
    /// <summary>
    /// Проверить, созданы ли поручения из задания.
    /// </summary>
    /// <param name="assignment">Задание, для которого проверить.</param>
    /// <returns>True, если поручения созданы, иначе false.</returns>
    [Remote(IsPure = true), Public]
    public static bool HasSubActionItems(IAssignment assignment)
    {
      var subActionItemExecutions = ActionItemExecutionTasks.GetAll()
        .Where(ai => Equals(ai.ParentAssignment, assignment));
      if (!subActionItemExecutions.Any())
        return true;
      
      return false;
    }
    
    /// <summary>
    /// Проверить, созданы ли поручения из задачи.
    /// </summary>
    /// <param name="task">Задача, для которой проверить.</param>
    /// <returns>True, если поручения созданы, иначе false.</returns>
    [Remote(IsPure = true), Public]
    public static bool HasSubActionItems(ITask task)
    {
      if (task == null)
        return false;
      
      var hasSubActionItem = ActionItemExecutionTasks.GetAll()
        .Where(a => a.ParentAssignment != null && Equals(a.ParentAssignment.Task, task))
        .Any();
      
      return hasSubActionItem;
    }
    
    /// <summary>
    /// Проверить, созданы ли поручения из задачи, с определенным значением жизненного цикла.
    /// </summary>
    /// <param name="task">Задача, для которой проверить.</param>
    /// <param name="status">Статус поручений.</param>
    /// <returns>True, если поручения созданы, иначе false.</returns>
    [Remote(IsPure = true), Public]
    public static bool HasSubActionItems(ITask task, Enumeration status)
    {
      if (task == null)
        return false;
      
      var hasSubActionItem = ActionItemExecutionTasks.GetAll()
        .Where(a => a.ParentAssignment != null && Equals(a.ParentAssignment.Task, task))
        .Any(a => a.Status == status);
      
      return hasSubActionItem;
    }
    
    /// <summary>
    /// Проверить, созданы ли поручения из задачи, с определенным значением жизненного цикла, с учетом, что "Выдал" адресат.
    /// </summary>
    /// <param name="task">Задача, для которой проверить.</param>
    /// <param name="status">Статус поручений.</param>
    /// <param name="addressee">Адресат.</param>
    /// <returns>True, если поручения созданы, иначе false.</returns>
    [Remote(IsPure = true), Public]
    public static bool HasSubActionItems(ITask task, Enumeration status, Sungero.Company.IEmployee addressee)
    {
      if (task == null)
        return false;
      
      var hasSubActionItem = ActionItemExecutionTasks.GetAll()
        .Where(a => a.ParentAssignment != null && Equals(a.ParentAssignment.Task, task))
        .Where(a => Equals(addressee, a.AssignedBy))
        .Any(a => a.Status == status);
      
      return hasSubActionItem;
    }
    
    /// <summary>
    /// Получить задания исполнителей, не завершивших работу по поручению.
    /// </summary>
    /// <param name="task"> Поручение, для которого требуется получить исполнителей.</param>
    /// <returns>Список исполнителей, не завершивших работу по поручению.</returns>
    [Remote(IsPure = true)]
    public static IQueryable<IActionItemExecutionAssignment> GetActionItems(Sungero.RecordManagement.IActionItemExecutionTask task)
    {
      return ActionItemExecutionAssignments
        .GetAll()
        .Where(a => task.IsCompoundActionItem == true && Equals(task, a.Task.ParentTask) ||
               task.IsCompoundActionItem != true && Equals(task, a.Task))
        .Where(a => a.Status == Workflow.AssignmentBase.Status.InProcess);
    }
    
    /// <summary>
    /// Получить список поручений для формирования блока резолюции задачи на согласование.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="status">Статус поручений (исключаемый).</param>
    /// <param name="addressee">Адресат.</param>
    /// <returns>Список поручений.</returns>
    [Remote(IsPure = true), Public]
    public static List<ITask> GetActionItemsForResolution(ITask task, Enumeration status, IEmployee addressee)
    {
      var actionItems = RecordManagement.ActionItemExecutionTasks.GetAll()
        .Where(t => Equals(t.ParentAssignment.Task, task) && t.Status != status && Equals(t.AssignedBy, addressee))
        .OrderBy(t => t.Started);
      
      var actionItemList = new List<ITask>();
      
      foreach (var actionItem in actionItems)
      {
        if (actionItem.IsCompoundActionItem == true)
        {
          foreach (var item in actionItem.ActionItemParts)
          {
            actionItemList.Add(item.ActionItemPartExecutionTask);
          }
        }
        else
        {
          actionItemList.Add(actionItem);
        }
      }
      
      return actionItemList;
    }

    /// <summary>
    /// Сформировать вспомогательную информацию по поручению для задачи на согласование.
    /// </summary>
    /// <param name="task">Задача на согласование.</param>
    /// <returns>Вспомогательная информация по поручению для задачи на согласование.</returns>
    [Remote(IsPure = true), Public]
    public static List<string> ActionItemInfoProvider(ITask task)
    {
      var result = new string[4];
      var actionItem = ActionItemExecutionTasks.As(task);
      if (task != null)
      {
        // Отчет пользователя. result[0]
        result[0] += actionItem.ActiveText;
        
        // Исполнители. result[1]
        if (actionItem.CoAssignees.Any())
          result[1] += string.Format("{0}: {1}, {2}: {3}",
                                     Docflow.Resources.StateViewResponsible,
                                     Company.PublicFunctions.Employee.GetShortName(actionItem.Assignee, false),
                                     Docflow.Resources.StateViewCoAssignees,
                                     string.Join(", ", actionItem.CoAssignees.Select(c => Company.PublicFunctions.Employee.GetShortName(c.Assignee, false))));
        else
          result[1] += string.Format("{0}: {1}", Docflow.Resources.StateViewAssignee, Company.PublicFunctions.Employee.GetShortName(actionItem.Assignee, false));
        
        // Срок. result[2]
        if (actionItem.MaxDeadline.HasValue)
          result[2] += string.Format(" {0}: {1}", Docflow.OfficialDocuments.Resources.StateViewDeadline, Docflow.PublicFunctions.Module.ToShortDateShortTime(actionItem.MaxDeadline.Value.ToUserTime()));
        
        // Контролер. result[3]
        if (actionItem.IsUnderControl == true)
        {
          result[3] += string.Format(" {0}: {1}", Docflow.OfficialDocuments.Resources.StateViewSupervisor, Company.PublicFunctions.Employee.GetShortName(actionItem.Supervisor, false));
        }
      }
      return result.ToList();
    }
    
    /// <summary>
    /// Получить исполнителей, не завершивших работу по поручению.
    /// </summary>
    /// <param name="entity"> Поручение, для которого требуется получить исполнителей.</param>
    /// <returns>Список исполнителей, не завершивших работу по поручению.</returns>
    [Remote(IsPure = true)]
    public static IQueryable<IUser> GetActionItemsPerformers(Sungero.RecordManagement.IActionItemExecutionTask entity)
    {
      return GetActionItems(entity).Select(p => p.Performer);
    }
    
    /// <summary>
    /// Выдать права на вложения поручения.
    /// </summary>
    /// <param name="attachmentGroup"> Группа вложения.</param>
    /// <param name="needGrantAccessRightsToPerformer"> Нужно ли выдать права исполнителю.</param>
    /// <remarks>Не используется, оставлен для совместимости.</remarks>
    [Obsolete("Используйте метод GrantAccessRightsToAttachments.")]
    public virtual void GrantRightsToAttachments(List<IEntity> attachmentGroup, bool needGrantAccessRightsToPerformer)
    {
      foreach (var item in attachmentGroup)
      {
        if (ElectronicDocuments.Is(item))
        {
          if (_obj.Author != null)
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.Author, DefaultAccessRightsTypes.Read);
          
          if (_obj.AssignedBy != null)
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.AssignedBy, DefaultAccessRightsTypes.Read);
          
          if (_obj.Supervisor != null)
          {
            var accessRightType = item.AccessRights.CanUpdate(_obj.Author) ? DefaultAccessRightsTypes.Change : DefaultAccessRightsTypes.Read;
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.Supervisor, accessRightType);
          }
          
          if (_obj.Assignee != null && needGrantAccessRightsToPerformer)
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.Assignee, DefaultAccessRightsTypes.Read);
          
          foreach (var observer in _obj.ActionItemObservers)
          {
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, observer.Observer, DefaultAccessRightsTypes.Read);
          }
        }
      }
    }
    
    /// <summary>
    /// Выдать права на вложения поручения.
    /// </summary>
    /// <param name="attachmentGroup"> Группа вложения.</param>
    /// <param name="needGrantAccessRightsToPerformer"> Нужно ли выдать права исполнителю.</param>
    public virtual void GrantAccessRightsToAttachments(List<IEntity> attachmentGroup, bool needGrantAccessRightsToPerformer)
    {
      foreach (var item in attachmentGroup)
      {
        if (ElectronicDocuments.Is(item))
        {
          if (_obj.Author != null)
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.Author, DefaultAccessRightsTypes.Read);
          
          if (_obj.AssignedBy != null)
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.AssignedBy, DefaultAccessRightsTypes.Read);
          
          if (_obj.Supervisor != null)
          {
            var accessRightType = item.AccessRights.CanUpdate(_obj.Author) ? DefaultAccessRightsTypes.Change : DefaultAccessRightsTypes.Read;
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.Supervisor, accessRightType);
          }
          
          if (_obj.Assignee != null && needGrantAccessRightsToPerformer)
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.Assignee, DefaultAccessRightsTypes.Read);
          
          foreach (var observer in _obj.ActionItemObservers)
          {
            Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, observer.Observer, DefaultAccessRightsTypes.Read);
          }
        }
      }
    }
    
    /// <summary>
    /// Выдать права исполнителю на вложения поручения.
    /// </summary>
    /// <param name="attachmentGroup"> Группа вложения.</param>
    public virtual void GrantAccessRightsToAssignee(List<IEntity> attachmentGroup)
    {
      foreach (var item in attachmentGroup)
      {
        if (ElectronicDocuments.Is(item) && _obj.Assignee != null)
          Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(item, _obj.Assignee, DefaultAccessRightsTypes.Read);
      }
    }
    
    /// <summary>
    /// Создать поручение из открытого задания.
    /// </summary>
    /// <param name="actionItemAssignment">Задание.</param>
    /// <returns>Поручение.</returns>
    [Remote(PackResultEntityEagerly = true)]
    public static IActionItemExecutionTask CreateActionItemExecutionFromExecution(Sungero.RecordManagement.IActionItemExecutionAssignment actionItemAssignment)
    {
      var actionItemAssignmentId = actionItemAssignment != null ? actionItemAssignment.Id : -1;
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Start CreateActionItemExecutionFromExecution.", actionItemAssignmentId);
      
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Get documents.", actionItemAssignmentId);
      IActionItemExecutionTask task;
      var document = actionItemAssignment.DocumentsGroup.OfficialDocuments.FirstOrDefault();
      var otherDocuments = actionItemAssignment.OtherGroup.All;
      
      // MainTask должен быть изменен до создания вложений и текстов задачи.
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Create task.", actionItemAssignmentId);
      if (document != null)
        task = Functions.Module.CreateActionItemExecution(document, actionItemAssignment);
      else
        task = ActionItemExecutionTasks.CreateAsSubtask(actionItemAssignment);
      
      // Для подчиненных поручений заполнить признак автовыполнения из персональных настроек.
      if (task.ParentAssignment != null)
      {
        /* _obj.AssignedBy пробрасывается при изменении в _obj.Author.
         * В _obj.StartedBy до старта задачи записывается тот, кто задачу создал.
         */
        var settings = Docflow.PublicFunctions.PersonalSetting.GetPersonalSettings(Employees.As(task.StartedBy));
        task.IsAutoExec = settings != null && (task.IsUnderControl != true || !Equals(task.Supervisor, task.StartedBy))
          ? settings.IsAutoExecLeadingActionItem
          : false;
      }
      
      var taskId = task != null ? task.Id : -1;
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Task (ID={1}) created.", actionItemAssignmentId, taskId);
      
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Add documents to OtherGroup. Task (ID={1}).", actionItemAssignmentId, taskId);
      foreach (var otherDocument in otherDocuments)
        if (!task.OtherGroup.All.Contains(otherDocument))
          task.OtherGroup.All.Add(otherDocument);
      
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Set Assignee = null. Task (ID={1}).", actionItemAssignmentId, taskId);
      task.Assignee = null;
      if (actionItemAssignment.Deadline.HasValue &&
          (actionItemAssignment.Deadline.Value.HasTime() && actionItemAssignment.Deadline >= Calendar.Now ||
           !actionItemAssignment.Deadline.Value.HasTime() && actionItemAssignment.Deadline >= Calendar.Today))
      {
        Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Set Deadline = {1}. Task (ID={2}).", actionItemAssignmentId, actionItemAssignment.Deadline, taskId);
        task.Deadline = actionItemAssignment.Deadline;
      }
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). Set AssignedBy = {1} (ID={2}). Task (ID={3}).",
                         actionItemAssignmentId, Users.Current, Users.Current.Id, taskId);
      task.AssignedBy = Employees.Current;
      Logger.DebugFormat("ActionItemExecutionAssignment (ID={0}). End CreateActionItemExecutionFromExecution.", actionItemAssignmentId);
      
      return task;
    }
    
    /// <summary>
    /// Выдать права на задачу контролеру, инициатору и группе регистрации инициатора ведущей задачи (включая ведущие ведущего).
    /// </summary>
    /// <param name="targetTask">Текущая задача.</param>
    /// <param name="sourceTask">Ведущая задача.</param>
    /// <returns>Текущую задачу с правами.</returns>
    public static IEntity GrantAccessRightToTask(IEntity targetTask, ITask sourceTask)
    {
      if (targetTask == null || sourceTask == null)
        return null;
      
      if (!ActionItemExecutionTasks.Is(sourceTask))
        sourceTask = GetLeadTaskToTask(sourceTask);
      
      var leadPerformers = Functions.ActionItemExecutionTask.GetLeadActionItemExecutionPerformers(ActionItemExecutionTasks.As(sourceTask));
      foreach (var performer in leadPerformers)
        Docflow.PublicFunctions.Module.GrantAccessRightsOnEntity(targetTask, performer, DefaultAccessRightsTypes.Change);
      
      return targetTask;
    }
    
    /// <summary>
    /// Выдать права на задание контролеру, инициатору и группе регистрации инициатора ведущей задачи (включая ведущие ведущего).
    /// </summary>
    /// <param name="targetAssignment">Текущее задание.</param>
    /// <param name="sourceTask">Ведущая задача.</param>
    /// <returns>Текущее задание с правами.</returns>
    [Remote, Public]
    public static IAssignment GrantAccessRightToAssignment(IAssignment targetAssignment, ITask sourceTask)
    {
      GrantAccessRightToTask(targetAssignment, sourceTask);
      targetAssignment.AccessRights.Save();
      return targetAssignment;
    }
    
    /// <summary>
    /// Получить всех контролеров, инициаторов (включая группу регистрации) ведущих задач.
    /// </summary>
    /// <param name="actionItemExecution">Поручение.</param>
    /// <returns>Список контролеров, инициаторов.</returns>
    public static List<IRecipient> GetLeadActionItemExecutionPerformers(Sungero.RecordManagement.IActionItemExecutionTask actionItemExecution)
    {
      var leadPerformers = new List<IRecipient>();
      var taskAuthors = new List<IRecipient>();
      ITask parentTask = actionItemExecution;
      
      while (true)
      {
        if (parentTask.StartedBy != null)
          taskAuthors.Add(parentTask.StartedBy);
        
        if (ActionItemExecutionTasks.Is(parentTask))
        {
          var parentActionItemExecution = ActionItemExecutionTasks.As(parentTask);
          taskAuthors.Add(parentActionItemExecution.Author);
          if (parentActionItemExecution.Supervisor != null)
            leadPerformers.Add(parentActionItemExecution.Supervisor);
          if (parentActionItemExecution.AssignedBy != null)
            leadPerformers.Add(parentActionItemExecution.AssignedBy);
        }
        else if (DocumentReviewTasks.Is(parentTask))
        {
          var parentDocumentReview = DocumentReviewTasks.As(parentTask);
          taskAuthors.Add(parentDocumentReview.Author);
        }
        else if (Sungero.Docflow.ApprovalTasks.Is(parentTask))
        {
          // TODO Добавить исполнителей согласования.
          var parentApprovalTask = Sungero.Docflow.ApprovalTasks.As(parentTask);
          taskAuthors.Add(parentApprovalTask.Author);
        }
        
        if (Equals(parentTask.MainTask, parentTask))
          break;
        parentTask = GetLeadTaskToTask(parentTask);
      }
      
      leadPerformers.AddRange(taskAuthors);
      var registrationGroup = Functions.ActionItemExecutionTask.GetExecutingDocumentRegistrationGroup(actionItemExecution);
      if (registrationGroup != null)
        leadPerformers.Add(registrationGroup);
      
      return leadPerformers.Distinct().ToList();
    }
    
    /// <summary>
    /// Получить ведущую задачу задачи.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Ведущая задача.</returns>
    public static ITask GetLeadTaskToTask(ITask task)
    {
      if (task.ParentAssignment != null)
        return task.ParentAssignment.Task;
      else
        return task.ParentTask ?? task.MainTask;
    }
    
    /// <summary>
    /// Получить нестандартных исполнителей задачи.
    /// </summary>
    /// <returns>Исполнители.</returns>
    public virtual List<IRecipient> GetTaskAdditionalAssignees()
    {
      var assignees = new List<IRecipient>();
      
      var registrationGroup = Functions.ActionItemExecutionTask.GetExecutingDocumentRegistrationGroup(_obj);
      if (registrationGroup != null)
        assignees.Add(registrationGroup);
      
      if (_obj.Assignee != null)
        assignees.Add(_obj.Assignee);
      
      if (_obj.Supervisor != null)
        assignees.Add(_obj.Supervisor);
      
      if (_obj.AssignedBy != null)
        assignees.Add(_obj.AssignedBy);
      
      assignees.AddRange(_obj.CoAssignees.Where(o => o.Assignee != null).Select(o => o.Assignee));
      assignees.AddRange(_obj.ActionItemParts.Where(o => o.Assignee != null).Select(o => o.Assignee));
      assignees.AddRange(_obj.ActionItemObservers.Where(o => o.Observer != null).Select(o => o.Observer));
      
      return assignees.Distinct().ToList();
    }
    
    /// <summary>
    /// Проверить документ на вхождение в обязательную группу вложений.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>True, если документ обязателен.</returns>
    public virtual bool DocumentInRequredGroup(Docflow.IOfficialDocument document)
    {
      return _obj.DocumentsGroup.OfficialDocuments.Any(d => Equals(d, document));
    }
    
    /// <summary>
    /// Добавить получателей в группу исполнителей поручения, исключая дублирующиеся записи.
    /// </summary>
    /// <param name="recipient">Реципиент.</param>
    /// <returns>Если возникли ошибки/хинты, возвращает текст ошибки, иначе - пустая строка.</returns>
    [Public, Remote]
    public string SetRecipientsToAssignees(IRecipient recipient)
    {
      var error = string.Empty;
      var performers = new List<IRecipient> { recipient };
      var employees = Company.PublicFunctions.Module.Remote.GetEmployeesFromRecipientsRemote(performers);
      if (employees.Count > Constants.ActionItemExecutionTask.MaxCompoundGroup)
        return ActionItemExecutionTasks.Resources.BigGroupWarningFormat(Sungero.RecordManagement.PublicConstants.ActionItemExecutionTask.MaxCompoundGroup);
      
      var currentPerformers = _obj.ActionItemParts.Select(x => x.Assignee);
      employees = employees.Except(currentPerformers).ToList();
      
      foreach (var employee in employees)
        _obj.ActionItemParts.AddNew().Assignee = employee;
      
      return error;
    }
    
    /// <summary>
    /// Получить состояние исполнения документа исключительно по этой задаче.
    /// </summary>
    /// <returns>Состояние исполнения документа исключительно по этой задаче.</returns>
    public virtual Enumeration? GetDocumentExecutionState()
    {
      // Статус "На исполнении".
      if (_obj.ExecutionState == Sungero.RecordManagement.ActionItemExecutionTask.ExecutionState.OnExecution ||
          _obj.ExecutionState == Sungero.RecordManagement.ActionItemExecutionTask.ExecutionState.OnRework ||
          _obj.ExecutionState == Sungero.RecordManagement.ActionItemExecutionTask.ExecutionState.OnControl)
        return Sungero.Docflow.OfficialDocument.ExecutionState.OnExecution;
      
      // Статус "Исполнен".
      if (_obj.ExecutionState == Sungero.RecordManagement.ActionItemExecutionTask.ExecutionState.Executed)
        return Sungero.Docflow.OfficialDocument.ExecutionState.Executed;
      
      // Статус "Прекращено".
      if (_obj.ExecutionState == Sungero.RecordManagement.ActionItemExecutionTask.ExecutionState.Aborted)
        return Sungero.Docflow.OfficialDocument.ExecutionState.Aborted;
      
      return null;
    }
    
    /// <summary>
    /// Добавить документы из группы "Результаты исполнения" в ведущее задание на исполнение.
    /// </summary>
    [Public, Remote]
    public virtual void SynchronizeResultGroup()
    {
      var parentAssignment = Functions.ActionItemExecutionTask.GetParentAssignment(_obj);
      if (parentAssignment != null && parentAssignment.Status != Workflow.Assignment.Status.Completed)
      {
        var documentGroup = parentAssignment.ResultGroup.OfficialDocuments;
        foreach (var document in _obj.ResultGroup.OfficialDocuments)
        {
          if (!documentGroup.Contains(document))
            documentGroup.Add(document);
        }
        parentAssignment.Save();

        // Выдать права на вложенные документы.
        var parentActionItem = ActionItemExecutionTasks.As(parentAssignment.Task);
        if (parentActionItem != null)
          Functions.ActionItemExecutionTask.GrantAccessRightsToAttachments(parentActionItem, parentAssignment.ResultGroup.All.ToList(), false);
      }
    }
    
    /// <summary>
    /// Добавить отчет исполнителей из подчиненных поручений в ведущее задание на исполнение.
    /// </summary>
    public virtual void SynchronizeResultActiveText()
    {
      var parentAssignment = Functions.ActionItemExecutionTask.GetParentAssignment(_obj);
      if (parentAssignment == null || parentAssignment.Status == Workflow.Assignment.Status.Completed)
        return;
      
      // Получить все подчиненные поручения.
      var subActionItems = ActionItemExecutionTasks.GetAll()
        .Where(t => Equals(t.ParentAssignment, parentAssignment) &&
               parentAssignment.Task.StartId == t.ParentStartId)
        .ToList();
      
      // Получить все пункты составных подпоручений.
      var compoundTasks = subActionItems.Where(i => i.IsCompoundActionItem == true);
      var subActionItemParts = ActionItemExecutionTasks.GetAll()
        .Where(t => ActionItemExecutionTasks.Is(t.ParentTask))
        .Where(t => compoundTasks.Contains(ActionItemExecutionTasks.As(t.ParentTask)) &&
               t.ParentTask.StartId == t.ParentStartId)
        .ToList();
      subActionItems.AddRange(subActionItemParts);
      
      var completedSubActionItems = subActionItems
        .Where(x => x.Status == Sungero.Workflow.Task.Status.Completed)
        .ToList();
      completedSubActionItems.Add(_obj);
      
      // Получить все задания на исполнение.
      var assignments = ActionItemExecutionAssignments.GetAll()
        .Where(x => ActionItemExecutionTasks.Is(x.Task) &&
               completedSubActionItems.Contains(ActionItemExecutionTasks.As(x.Task)) &&
               Equals(x.TaskStartId, x.Task.StartId))
        .ToList();
      
      // Сформировать общий отчет.
      var activeTextItems = new List<string>();
      activeTextItems.Add(this.GetParentAssignmentOwnActiveText(parentAssignment));
      activeTextItems.AddRange(this.GetSubActionItemsActiveTexts(assignments));
      var separator = string.Format("{0}{0}", Environment.NewLine);
      parentAssignment.ActiveText = string.Join(separator, activeTextItems);
      parentAssignment.Save();
    }
    
    /// <summary>
    /// Получить собственную часть ActiveText ведущего задания.
    /// </summary>
    /// <param name="assignment">Ведущее задание.</param>
    /// <returns>Собственная часть ActiveText ведущего задания.</returns>
    public virtual string GetParentAssignmentOwnActiveText(IActionItemExecutionAssignment assignment)
    {
      if (string.IsNullOrWhiteSpace(assignment.ActiveText))
        return ActionItemExecutionTasks.Resources.ActionItemExecutionExecutedLabel;
      return assignment.ActiveText;
    }
    
    /// <summary>
    /// Получить коллекцию ActiveText по подчиненным поручениям.
    /// </summary>
    /// <param name="assignments">Подчиненные задания на исполнение поручения.</param>
    /// <returns>Коллекция ActiveText по подчиненным поручениям.</returns>
    /// <remarks>Для каждого поручения ActiveText будет преобразован к формату
    /// Фамилия И.О.:_ActiveText.</remarks>
    public virtual List<string> GetSubActionItemsActiveTexts(List<IActionItemExecutionAssignment> assignments)
    {
      return assignments
        .GroupBy(x => Company.PublicFunctions.Employee.GetShortName(ActionItemExecutionTasks.As(x.Task).Assignee, true))
        .Select(x => string.Format("{0}: {1}",
                                   x.Key,
                                   string.Join(Environment.NewLine, x.OrderBy(a => a.Task.Id).ThenBy(a => a.IterationId).Select(a => a.ActiveText))))
        .ToList();
    }

    /// <summary>
    /// Выполнить ведущее задание на исполнение поручения.
    /// </summary>
    [Public, Remote]
    public virtual void CompleteParentAssignment()
    {
      var assignment = Functions.ActionItemExecutionTask.GetParentAssignment(_obj);
      if (assignment != null && assignment.Status != Workflow.Assignment.Status.Completed)
      {
        Logger.DebugFormat("ActionItemExecutionAssignment(ID={0}) completed automatically from ActionItemExecutionTask(ID={1}). (Result=Done)",
                           assignment.Id,
                           _obj.Id);
        assignment.Complete(Sungero.RecordManagement.ActionItemExecutionAssignment.Result.Done);
        var currentUser = Users.Current;
        if (currentUser != null && currentUser.IsSystem == true)
        {
          var performer = assignment.Performer;
          Logger.DebugFormat("ActionItemExecutionAssignment(ID={0}) performer: {1}(ID={2}).", assignment.Id, performer.DisplayValue, performer.Id);
          Logger.DebugFormat("ActionItemExecutionAssignment(ID={0}) completed by {1}(ID={2}). Set CompletedBy to {3}(ID={4}).",
                             assignment.Id,
                             currentUser.DisplayValue, currentUser.Id,
                             performer.DisplayValue, performer.Id);
          assignment.CompletedBy = performer;
        }
      }
    }
    
    /// <summary>
    /// Выполнить блоки мониторинга составного поручения.
    /// </summary>
    public virtual void ExecuteParentActionItemExecutionTaskMonitorings()
    {
      var task = _obj.ParentTask;
      if (task == null || !ActionItemExecutionTasks.Is(task))
        return;
      var actionItem = ActionItemExecutionTasks.As(task);
      if (actionItem.IsCompoundActionItem == true &&
          Functions.ActionItemExecutionTask.AllActionItemPartsAreCompleted(actionItem))
      {
        Logger.DebugFormat("ActionItemExecutionTask(ID={0}) Call ExecuteAllMonitoringBlocks of ParentTask(ID={1})", _obj.Id, actionItem.Id);
        actionItem.Blocks.ExecuteAllMonitoringBlocks();
      }
    }
    
    /// <summary>
    /// Проверить, выполнены ли все пункты составного поручения.
    /// </summary>
    /// <returns>True, если все пункты составного поручения выполнены, иначе - False.</returns>
    [Remote(IsPure = true)]
    public virtual bool AllActionItemPartsAreCompleted()
    {
      return !ActionItemExecutionTasks.GetAll(j => Equals(j.ParentTask, _obj) &&
                                              j.Status.Value != Workflow.Task.Status.Aborted &&
                                              j.Status.Value != Workflow.Task.Status.Completed &&
                                              j.Status.Value != Workflow.Task.Status.Draft &&
                                              j.ParentStartId == _obj.StartId).Any();
    }
    
    /// <summary>
    /// Проверить, выполнены ли все пункты составного поручения, кроме текущего.
    /// </summary>
    /// <returns>True, если все пункты составного поручения, кроме текущего, выполнены, иначе - False.</returns>
    [Remote(IsPure = true)]
    public virtual bool AllOtherActionItemPartsAreCompleted()
    {
      if (_obj.ParentTask == null)
        throw AppliedCodeException.Create(string.Format("ActionItemExecutionTask (ID = {0}) parent task is null.", _obj.Id));
      
      return !ActionItemExecutionTasks.GetAll(j => Equals(j.ParentTask, _obj.ParentTask) &&
                                              j.Status.Value != Workflow.Task.Status.Aborted &&
                                              j.Status.Value != Workflow.Task.Status.Completed &&
                                              j.Status.Value != Workflow.Task.Status.Draft &&
                                              j.Id != _obj.Id &&
                                              j.ParentStartId == _obj.ParentTask.StartId).Any();
    }
    
    /// <summary>
    /// Проверить, можно ли автоматически выполнить ведущее поручение.
    /// </summary>
    /// <returns>True, если можно автоматически выполнить ведущее поручение, иначе - False.</returns>
    public virtual bool CanAutoExecParentAssignment()
    {
      // У составного поручения IsAutoExec не пробрасывается в пункты и имеет смысл только для задачи-контейнера.
      var parentTask = ActionItemExecutionTasks.As(_obj.ParentTask);
      if (parentTask == null && _obj.IsAutoExec == false ||
          parentTask != null && parentTask.IsCompoundActionItem == true && parentTask.IsAutoExec == false)
        return false;
      
      return this.AllOtherActionItemExecutionSubTasksAreCompleted();
    }
    
    /// <summary>
    /// Проверить, выполнены ли все подчиненные поручения, кроме текущего.
    /// </summary>
    /// <returns>True, если все подчиненные поручения, кроме текущего, выполнены, иначе - False.</returns>
    [Public, Remote(IsPure = true)]
    public virtual bool AllOtherActionItemExecutionSubTasksAreCompleted()
    {
      var assignment = Functions.ActionItemExecutionTask.GetParentAssignment(_obj);
      if (assignment == null)
        return false;
      
      // Проверить наличие других невыполненных подчиненных поручений у ведущего задания.
      var otherActionItems = Functions.ActionItemExecutionTask.GetSubActionItemExecutions(assignment)
        .Where(x => x.Id != _obj.Id).ToList();
      
      if (otherActionItems.Any(x => x.IsCompoundActionItem != true))
        return false;
      
      // Не давать выполнять задание, если есть незавершенные пункты составных подчиненных поручений.
      foreach (var actionItem in otherActionItems)
      {
        if (Functions.ActionItemExecutionTask.AllActionItemPartsAreCompleted(actionItem))
          continue;
        if (!actionItem.ActionItemParts.Any(p => p.ActionItemPartExecutionTask != null &&
                                            p.ActionItemPartExecutionTask.Id == _obj.Id))
          return false;
        if (!Functions.ActionItemExecutionTask.AllOtherActionItemPartsAreCompleted(_obj))
          return false;
      }
      
      return true;
    }

    /// <summary>
    /// Получить задание на исполнение.
    /// </summary>
    /// <returns>Задание на исполнение.</returns>
    /// <remarks>Сортировка по дате создания нужна для того,
    /// чтобы выбиралось актуальное задание по текущему пользователю,
    /// так как возможна ситуация, что в результате корректировок он становился исполнителем более одного раза.</remarks>
    public virtual IActionItemExecutionAssignment GetActionItemExecutionAssignment()
    {
      return ActionItemExecutionAssignments.GetAll()
        .Where(j => Equals(j.Task, _obj))
        .Where(j => Equals(j.Performer, _obj.Assignee))
        .Where(j => j.TaskStartId == _obj.StartId)
        .OrderByDescending(j => j.Created)
        .FirstOrDefault();
    }
    
    /// <summary>
    /// Получить задание на приемку.
    /// </summary>
    /// <returns>Задание на приемку.</returns>
    /// <remarks>Сортировка по дате создания нужна для того,
    /// чтобы выбиралось актуальное задание по текущему пользователю,
    /// так как возможна ситуация, что в результате корректировок он становился контролером более одного раза.</remarks>
    public virtual IActionItemSupervisorAssignment GetActionItemSupervisorAssignment()
    {
      return ActionItemSupervisorAssignments.GetAll()
        .Where(j => Equals(j.Task, _obj))
        .Where(j => Equals(j.Performer, _obj.Supervisor))
        .Where(j => j.TaskStartId == _obj.StartId)
        .OrderByDescending(j => j.Created)
        .FirstOrDefault();
    }
    
    /// <summary>
    /// Получить активное задание на исполнение актуальному исполнителю поручения.
    /// </summary>
    /// <returns>Задание на исполнение.</returns>
    public virtual IActionItemExecutionAssignment GetActualActionItemExecutionAssignment()
    {
      return ActionItemExecutionAssignments.GetAll()
        .Where(j => Equals(j.Task, _obj))
        .Where(j => j.Status == Workflow.AssignmentBase.Status.InProcess)
        .Where(j => Equals(j.Performer, _obj.Assignee))
        .Where(j => j.TaskStartId == _obj.StartId)
        .FirstOrDefault();
    }
    
    /// <summary>
    /// Получить активное задание на приемку актуальному контролеру поручения.
    /// </summary>
    /// <returns>Задание на приемку.</returns>
    public virtual IActionItemSupervisorAssignment GetActualActionItemSupervisorAssignment()
    {
      return ActionItemSupervisorAssignments.GetAll()
        .Where(j => Equals(j.Task, _obj))
        .Where(j => j.Status == Workflow.AssignmentBase.Status.InProcess)
        .Where(j => Equals(j.Performer, _obj.Supervisor))
        .Where(j => j.TaskStartId == _obj.StartId)
        .FirstOrDefault();
    }
    
    /// <summary>
    /// Получить все задания на приемку по текущей задаче, находящиеся в работе.
    /// </summary>
    /// <returns>Задания на приемку.</returns>
    public virtual List<IActionItemSupervisorAssignment> GetActionItemSupervisorAssignmentsInProcess()
    {
      return ActionItemSupervisorAssignments.GetAll()
        .Where(j => Equals(j.Task, _obj))
        .Where(j => j.Status == Workflow.AssignmentBase.Status.InProcess)
        .Where(j => j.TaskStartId == _obj.StartId)
        .ToList();
    }
    
    /// <summary>
    /// Получить все поручения соисполнителям.
    /// </summary>
    /// <param name="parentAssignment">Родительское задание.</param>
    /// <returns>Поручения соисполнителям.</returns>
    public virtual List<IActionItemExecutionTask> GetCoAssigneeActionItemExecutionTasks(IAssignment parentAssignment)
    {
      return ActionItemExecutionTasks.GetAll()
        .Where(t => t.ParentAssignment != null && Equals(t.ParentAssignment.Task, _obj))
        .Where(t => t.ActionItemType == ActionItemType.Additional)
        .Where(t => Equals(t.ParentAssignment, parentAssignment))
        .ToList();
    }
    
    /// <summary>
    /// Получить актуальных контролера и исполнителя поручения.
    /// </summary>
    /// <returns>Список, состоящий из контролера и исполнителя поручения.</returns>
    public virtual List<IEmployee> GetActualSupervisorAndAssignee()
    {
      var employees = new List<IEmployee>();
      
      if (_obj.Supervisor != null)
        employees.Add(_obj.Supervisor);
      if (_obj.Assignee != null)
        employees.Add(_obj.Assignee);
      
      return employees;
    }
  }
}