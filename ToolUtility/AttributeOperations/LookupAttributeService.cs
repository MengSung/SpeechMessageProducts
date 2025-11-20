using System;
using Microsoft.Xrm.Sdk;
using System.Linq;

namespace ToolUtilityNameSpace.AttributeOperations
{
    public class LookupAttributeService
    {
        private readonly object _logger;

        public LookupAttributeService(object logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Guid GetAttribute(Entity entity, string propertyName)
        {
            if (entity == null) return Guid.Empty;
            if (string.IsNullOrEmpty(propertyName)) return Guid.Empty;

            if (entity.Attributes.Contains(propertyName))
            {
                var value = entity.Attributes[propertyName];
                if (value is EntityReference er) return er.Id;
                try
                {
                    // attempt to parse if value is Guid or string
                    if (value is Guid g) return g;
                    var s = value.ToString();
                    if (Guid.TryParse(s, out var parsed)) return parsed;
                }
                catch (Exception ex)
                {
                    SafeLogError(ex, "GetAttribute lookup invalid cast for {0}", propertyName);
                    throw;
                }
            }

            return Guid.Empty;
        }

        public void SetAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));
            if (string.IsNullOrEmpty(lookupEntityName)) throw new ArgumentNullException(nameof(lookupEntityName));

            var reference = new EntityReference(lookupEntityName, guidValue);

            if (entity.Attributes.Contains(propertyName))
            {
                entity.Attributes[propertyName] = reference;
            }
            else
            {
                entity.Attributes.Add(propertyName, reference);
            }
        }

        private void SafeLogError(Exception ex, string format, params object[] args)
        {
            try
            {
                if (_logger == null) return;
                var loggerType = _logger.GetType();
                var logMethod = loggerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Log" && m.GetParameters().Length == 5 && m.IsGenericMethod);

                if (logMethod != null)
                {
                    var genericMethod = logMethod.MakeGenericMethod(typeof(object));
                    var logLevelType = Type.GetType("Microsoft.Extensions.Logging.LogLevel, Microsoft.Extensions.Logging.Abstractions");
                    object errorLevel = null;
                    if (logLevelType != null)
                    {
                        errorLevel = Enum.Parse(logLevelType, "Error");
                    }

                    var eventIdType = Type.GetType("Microsoft.Extensions.Logging.EventId, Microsoft.Extensions.Logging.Abstractions");
                    object eventId = null;
                    if (eventIdType != null)
                    {
                        eventId = Activator.CreateInstance(eventIdType, 0, string.Empty);
                    }

                    object state = string.Format(format, args);
                    Func<object, Exception, string> formatter = (s, e) => s?.ToString() ?? string.Empty;

                    var parameters = new object[] { errorLevel, eventId, state, ex, formatter };
                    genericMethod.Invoke(_logger, parameters);
                }
            }
            catch
            {
                // swallow
            }
        }
    }
}
