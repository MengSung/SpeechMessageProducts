using System;
using Microsoft.Xrm.Sdk;
using System.Linq;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 布林屬性專責服務
    /// </summary>
    public class BoolAttributeService
    {
        private readonly object _logger;

        public BoolAttributeService(object logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool GetAttribute(Entity entity, string propertyName)
        {
            try
            {
                if (entity == null) return false;
                if (string.IsNullOrEmpty(propertyName)) return false;

                if (entity.Attributes.Contains(propertyName))
                {
                    var value = entity.Attributes[propertyName];
                    if (value is bool b) return b;

                    // attempt to convert
                    try
                    {
                        return Convert.ToBoolean(value);
                    }
                    catch (Exception ex)
                    {
                        SafeLogError(ex, "GetAttribute: invalid cast for {0}", propertyName);
                        throw;
                    }
                }

                return false;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void SetAttribute(ref Entity entity, string propertyName, bool value)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            try
            {
                if (entity.Attributes.Contains(propertyName))
                {
                    entity.Attributes[propertyName] = value;
                }
                else
                {
                    entity.Attributes.Add(propertyName, value);
                }
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "SetAttribute failed for {0}", propertyName);
                throw;
            }
        }

        public void SetAttributeToNull(ref Entity entity, string propertyName)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            if (entity.Attributes.Contains(propertyName))
            {
                entity.Attributes[propertyName] = null;
            }
            else
            {
                entity.Attributes.Add(propertyName, null);
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
                // swallow logging errors
            }
        }
    }
}
