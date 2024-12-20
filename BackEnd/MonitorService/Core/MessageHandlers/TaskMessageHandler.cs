using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MonitorService.Core.Message;

namespace MonitorService.Core.MessageHandlers
{
  public class TaskMessageHandler : MessageHandler<TaskBackMessage, TaskMessageHandler, TaskMessage>
  {
    private readonly TracerContainer _container;
    public TaskMessageHandler(TracerContainer container, ILogger<TaskMessageHandler> logger) : base(logger)
    {
      _container = container;
    }

    public override async Task<TaskBackMessage> Handle(TaskMessage message)
    {
      var tracers = _container.Search(message.Endpoint);
      switch(message.State)
      {
        case 0:
          // Set the start version to monitor
          foreach(var tracer in tracers)
          {
            await tracer.UpdateCurrentVersion();
            _logger.LogInformation("{name} monitor task started in {endpoint}.", message.Name, message.Endpoint);
          }
          return new TaskBackMessage { Id = message.Id, State = 1, Content = string.Empty};
        case 2:
          // Get the Data changes
          var changeSets = tracers.Select(tracer => tracer.GetDataChanges());
          var changeJson = UnionDataSets(changeSets.ToArray());
          if(string.IsNullOrEmpty(changeJson))
          {
            _logger.LogWarning("No data changes in {task}.", $"{message.Endpoint}.{message.Name}");
          }
          else{
            _logger.LogDebug("The data changes in {task} is {content}.", $"{message.Endpoint}.{message.Name}", changeJson);
          }
          return new TaskBackMessage { Id = message.Id, State = 3, Content = changeJson };
      }
      return default;
    }

    public string UnionDataSets(params DataSet[] dataSets)
    {
        DataSet unionDataSet = new DataSet();

        foreach (DataSet ds in dataSets)
        {
            foreach (DataTable table in ds.Tables)
            {
                // If the table does not exist in unionDataSet, clone and add it.
                if (!unionDataSet.Tables.Contains(table.TableName))
                {
                    unionDataSet.Tables.Add(table.Clone());
                }
                
                // Merge rows from the current DataTable to the corresponding table in unionDataSet.
                unionDataSet.Tables[table.TableName].Merge(table);
            }
        }

        var jsonResult = new Dictionary<string, List<Dictionary<string, object>>>();
        foreach(DataTable table in unionDataSet.Tables)
        {
          var tableData = new List<Dictionary<string, object>>();
          var tableName = string.Empty;
          foreach( DataRow row in table.Rows)
          {
            var rowData = new Dictionary<string, object>();
            foreach(DataColumn column in table.Columns)
            {
              if(column.ColumnName == "TableName"){
                tableName = row[column].ToString();
              }
              else
              {
                rowData[column.ColumnName]= row[column];
              }
            }
            tableData.Add(rowData);
          }
          jsonResult[tableName] = tableData;
        }
        var options = new JsonSerializerOptions { WriteIndented = false };
        return JsonSerializer.Serialize(jsonResult, options);
    }
  }
}