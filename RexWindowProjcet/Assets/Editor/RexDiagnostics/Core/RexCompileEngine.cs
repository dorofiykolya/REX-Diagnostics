﻿using Rex.Utilities;
using Rex.Utilities.Helpers;
using Rex.Utilities.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

[Serializable]
public class RexCompileEngine : ScriptableObject, IDisposable
{
    public const float TIME_OUT_FOR_COMPILE_SEC = 2;

    [SerializeField]
    private volatile int _compileThreadID = -1;

    /// <summary>
    /// The continues compiled result by the <see cref="_compileThread"/>.
    /// </summary>
    private static volatile CompiledExpression _currentCompiledExpression;
    public static readonly object CompilerLockObject = new object();

    public void OnEnable()
    {
        hideFlags = HideFlags.HideAndDontSave;

        var _compileThread = new Thread(CompilerMainThread);
        _compileThread.Start();
        _compileThread.Name = "REX Compiler thread";
        _compileThreadID = _compileThread.ManagedThreadId;
    }

    public void Dispose()
    {
        _compileThreadID = -1;
        DestroyImmediate(this);
    }

    private void CompilerMainThread()
    {
        Debug.Log("Running Compiler thread! " + Thread.CurrentThread.ManagedThreadId, this);
        var lastCode = "";
        CompileJob lastJob = null;
        Thread lastThread = null;
        var activeThreads = new List<Thread>();
        while (_compileThreadID == Thread.CurrentThread.ManagedThreadId)
        {
            activeThreads.RemoveAll(i => (i.ThreadState & ThreadState.Stopped) != 0);
            Thread.Sleep(1);
            if (!string.IsNullOrEmpty(RexISM.Code) &&
                lastCode != RexISM.Code)
            {
                lastCode = RexISM.Code;
                if (lastJob != null)
                {
                    lastJob.RequestStop();
                    lastThread.Abort();
                }
                lastJob = new CompileJob();
                lastThread = new Thread(lastJob.CompileCode);
                lastThread.Start(lastCode);
                activeThreads.Add(lastThread);
            }
        }
        if (activeThreads.Count > 0)
        {
            foreach (var thread in activeThreads)
            {
                thread.Join();
            }
        }
        Debug.Log("Compiler thread finished! " + Thread.CurrentThread.ManagedThreadId, this);
    }

    public CompiledExpression GetCompile(string code)
    {
        var startedWaiting = DateTime.Now;
        while (true)
        {
            lock (CompilerLockObject)
            {
                if (_currentCompiledExpression != null &&
                    _currentCompiledExpression.Parse.WholeCode == code)
                {
                    if (_currentCompiledExpression.Errors.Count > 0)
                    {
                        RexHelper.Messages[MsgType.Error].AddRange(_currentCompiledExpression.Errors);
                        return null;
                    }

                    return _currentCompiledExpression;
                }
            }
            Thread.Sleep(10);
            if (DateTime.Now - startedWaiting > TimeSpan.FromSeconds(TIME_OUT_FOR_COMPILE_SEC))
            {
                RexHelper.Messages[MsgType.Error].Add("Time out on compiling expression, " + code);
                return null;
            }
        }
    }

    private class CompileJob
    {
        public void CompileCode(object code)
        {
            var parseResult = RexHelper.ParseAssigment((string)code);
            var result = RexHelper.Compile(parseResult);
            if (!_shouldStop)
            {
                lock (CompilerLockObject)
                {
                    _currentCompiledExpression = result;
                }
            }
        }

        public void RequestStop()
        {
            _shouldStop = true;
        }

        private volatile bool _shouldStop;
    }
}