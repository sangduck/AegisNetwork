﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;



namespace Aegis.Threading
{
    public sealed class WorkerThread
    {
        private BlockingQueue<Action> _works = new BlockingQueue<Action>();
        private Boolean _running;
        private Thread[] _threads;

        public String Name { get; private set; }
        public Int32 QueuedCount { get { return _works.Count; } }
        public Int32 ThreadCount { get { return _threads?.Count() ?? 0; } }





        public WorkerThread(String name)
        {
            Name = name;
        }


        public void Start(Int32 threadCount)
        {
            if (threadCount < 1)
                return;


            lock (this)
            {
                _works.Clear();

                _running = true;
                _threads = new Thread[threadCount];
                for (Int32 i = 0; i < threadCount; ++i)
                {
                    _threads[i] = new Thread(Run);
                    _threads[i].Name = String.Format("{0} {1}", Name, i);
                    _threads[i].Start();
                }
            }
        }


        public void Stop()
        {
            lock (this)
            {
                if (_running == false || _threads == null)
                    return;


                _running = false;
                _works.Cancel();

                foreach (Thread th in _threads)
                    th.Join();
                _threads = null;
            }
        }


        public void Post(Action item)
        {
            _works.Enqueue(item);
        }


        private void Run()
        {
            while (_running)
            {
                try
                {
                    Action item = _works.Dequeue();
                    if (item == null)
                        break;

                    item();
                }
                catch (JobCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Logger.Write(LogType.Err, 1, e.ToString());
                }
            }
        }
    }
}
