using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Company;
using Sungero.Core;
using Sungero.CoreEntities;

namespace Sungero.RecordManagement.Structures.ActionItemExecutionTask
{
  /// <summary>
  /// Модель контрола состояния задачи на исполнение поручения: кэш статусов, задачи, задания.
  /// </summary>
  [Public]
  partial class StateViewModel
  {
    
    public System.Collections.Generic.Dictionary<Sungero.Core.Enumeration?, string> StatusesCache { get; set; }
    
    public List<IActionItemExecutionTask> Tasks { get; set; }
    
    public List<Sungero.Workflow.IAssignment> Assignments { get; set; }
    
  }
  
  /// <summary>
  /// Изменения поручения.
  /// </summary>
  [Public]
  partial class ActionItemChanges
  {
    /// <summary>
    /// Старый контролер.
    /// </summary>
    public IEmployee OldSupervisor { get; set; }
    
    /// <summary>
    /// Новый контролер.
    /// </summary>
    public IEmployee NewSupervisor { get; set; }
    
    /// <summary>
    /// Старый исполнитель.
    /// </summary>
    public IEmployee OldAssignee { get; set; }
    
    /// <summary>
    /// Новый исполнитель.
    /// </summary>
    public IEmployee NewAssignee { get; set; }
    
    /// <summary>
    /// Старые соисполнители.
    /// </summary>
    public List<IEmployee> OldCoAssignees { get; set; }
    
    /// <summary>
    /// Новые соисполнители.
    /// </summary>
    public List<IEmployee> NewCoAssignees { get; set; }
    
    /// <summary>
    /// Дополнительная информация для использования в перекрытиях.
    /// </summary>
    public string AdditionalInfo { get; set; }
  }
}