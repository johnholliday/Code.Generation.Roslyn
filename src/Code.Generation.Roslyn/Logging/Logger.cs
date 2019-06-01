﻿// TODO: TBD: ditto licensing...
// Copyright (c) 2019 Michael W. Powell. All rights reserved.
// Licensed under the MS-PL license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Code.Generation.Roslyn.Logging
{
    // TODO: TBD: for now working through Console, but we might consider using a furnished TextWriter...
    using static Activator;
    using static Console;
    using static Constants;
    using static StringSplitOptions;

    /// <summary>
    /// Logs messages in MSBuild-recognized format to standard output.
    /// </summary>
    public class Logger
    {
        /// <summary>
        /// 0
        /// </summary>
        private const int InformationLevel = 0;

        /// <summary>
        /// 1
        /// </summary>
        private const int WarningLevel = 1;

        /// <summary>
        /// 2
        /// </summary>
        private const int ErrorLevel = 2;

        private TextWriter OutputWriter { get; }

        // ReSharper disable once RedundantEmptyObjectOrCollectionInitializer
        private static Lazy<Logger> LazyResource { get; } = new Lazy<Logger>(CreateInstance<Logger>);

        public static Logger Resource => LazyResource.Value;

        private static void DefaultInformationalLoggerCallback(TextWriter writer, int logLevel, string message, string diagnosticCode)
            => writer.WriteLine(message);

        // ReSharper disable once StringLiteralTypo
        private static void DefaultWarningOrErrorLoggerCallback(TextWriter writer, int logLevel, string message, string diagnosticCode)
            => writer.WriteLine($"dotnet-codegen[{logLevel}]: [{diagnosticCode}] {message}");

        private Logger() : this(Out
            , DefaultInformationalLoggerCallback
            , DefaultWarningOrErrorLoggerCallback
            , DefaultWarningOrErrorLoggerCallback
        )
        {
        }

        private static IEnumerable<T> GetRange<T>(params T[] values)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var value in values)
            {
                yield return value;
            }
        }

        private Logger(TextWriter outputWriter
            , LoggerCallback onInformationCallback
            , LoggerCallback onWarningCallback
            , LoggerCallback onErrorCallback)
        {
            OutputWriter = outputWriter;

            var callbackLevel = 0;

            LoggerCallbacks = GetRange(onInformationCallback, onWarningCallback, onErrorCallback)
                .ToDictionary(_ => callbackLevel++, x => x);
        }

        private IDictionary<int, LoggerCallback> LoggerCallbacks { get; }

        /// <summary>
        /// Log message to build output with <see cref="ErrorLevel"/>. Will fail build.
        /// </summary>
        /// <param name="message">Message to log. May be Line Feed or Carriage Return Line Feed delimited.</param>
        /// <param name="diagnosticCode">Prepends logger message with the diagnostic code.</param>
        public void Error(string message, string diagnosticCode = null) => Log(OutputWriter, ErrorLevel, message, diagnosticCode);

        /// <summary>
        /// Log message to build output with <see cref="WarningLevel"/>. May fail build.
        /// </summary>
        /// <param name="message">Message to log. May be Line Feed or Carriage Return Line Feed delimited.</param>
        /// <param name="diagnosticCode">Prepends logger message with the diagnostic code.</param>
        public void Warning(string message, string diagnosticCode = null) => Log(OutputWriter, WarningLevel, message, diagnosticCode);

        /// <summary>
        /// Log message to build output with <see cref="InformationLevel"/>.
        /// </summary>
        /// <param name="message">Message to log. May be Line Feed or Carriage Return Line Feed delimited.</param>
        public void Information(string message) => Log(OutputWriter, InformationLevel, message);

        /// <summary>
        /// Log message to build output.
        /// </summary>
        /// <param name="writer">A Text Writer instance.</param>
        /// <param name="requestLevel">Request level used to filter the logging request.</param>
        /// <param name="message">Message to log.</param>
        /// <param name="diagnosticCode">Prepends logger message with the diagnostic code.</param>
        public void Log(TextWriter writer, int requestLevel, string message, string diagnosticCode = null)
        {
            bool TryGetCallback(int level, out LoggerCallback triedCallback)
            {
                triedCallback = null;

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var (_, value) in LoggerCallbacks.OrderByDescending(x => x.Key).Where(x => x.Key >= level))
                {
                    triedCallback = value;
                    break;
                }

                return triedCallback != null;
            }

            if (!TryGetCallback(requestLevel, out var callback))
            {
                return;
            }

            // TODO: TBD: this is safe to do x-plat?
            var lines = message.Replace(CarriageReturnLineFeed, LineFeed)
                .Split(GetRange(LineFeed).ToArray(), None);

            lines.ToList().ForEach(line => callback.Invoke(writer, requestLevel, line, diagnosticCode));
        }
    }

    internal static class LoggerExtensionMethods
    {
        /// <summary>
        /// Deconstructs the <see cref="KeyValuePair{TKey,TValue}"/>.
        /// </summary>
        /// <param name="pair"></param>
        /// <param name="level"></param>
        /// <param name="callback"></param>
        /// <remarks>Should be able to use deconstruction &quot;naturally&quot;,
        /// but for whatever reason this is necessary.</remarks>
        public static void Deconstruct(this KeyValuePair<int, LoggerCallback> pair, out int level
            , out LoggerCallback callback)
            => (level, callback) = pair;
    }
}