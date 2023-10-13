using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Utilities
{
    public static class LogHelper
    {
        //public void Log(Exception ex)
        //{
        //    string InnerMessage = ex.InnerException is null ? string.Empty : $"Inner Exception=({ex.InnerException.GetType().Name} Inner Exception Message: '{ex.InnerException.Message}'";

        //    _logger.LogError("{StackTrace} | Exception=({Exception}) | Exception Message: '{Message}' | {InnerException}", ex.StackTrace, ex.GetType().Name, ex.Message, InnerMessage);
        //}

        /// <summary>
        /// Throw a NullReferenceException formatted for traversing JSON.
        /// </summary>
        /// <param name="objectName">Use nameof(object)</param>
        /// <param name="nodeName">Use nameof(node)</param>
        /// <param name="key">Key of node</param>
        public static string NullJsonNodeExceptionMessage(string objectName, string nodeName, string key)
        {
            return $"{objectName} is null. Failed to parse {nodeName}['{key}'], check key. ";
        }

        public static string NullHtmlNodeExceptionMessage(string objectName, string nodeName, string key)
        {
            return "";
        }
    }
}
