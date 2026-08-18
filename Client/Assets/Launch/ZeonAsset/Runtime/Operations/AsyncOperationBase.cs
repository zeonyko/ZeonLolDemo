using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 全异步操作基类：协程 / await / Cancel / Completed。
    /// </summary>
    public abstract class AsyncOperationBase : CustomYieldInstruction, INotifyCompletion, IEnumerator
    {
        private Action _continuation;
        private Action<AsyncOperationBase> _completed;

        public EOperationStatus Status { get; private set; } = EOperationStatus.None;
        public string Error { get; private set; }
        public float Progress { get; protected set; }
        public bool IsDone =>
            Status == EOperationStatus.Succeed ||
            Status == EOperationStatus.Failed ||
            Status == EOperationStatus.Canceled;
        public bool IsSucceed => Status == EOperationStatus.Succeed;
        public bool IsCanceled => Status == EOperationStatus.Canceled;
        public bool CancellationRequested { get; private set; }

        /// <summary>失败日志用的步骤名，如 VersionCheck / Download。</summary>
        protected virtual string DiagnosticStep => null;

        internal string GetDiagnosticLabel()
        {
            var name = GetType().Name;
            var step = DiagnosticStep;
            return string.IsNullOrEmpty(step) ? name : name + "." + step;
        }

        internal virtual void AppendFailContext(StringBuilder sb)
        {
        }

        public event Action<AsyncOperationBase> Completed
        {
            add
            {
                if (IsDone)
                    value?.Invoke(this);
                else
                    _completed += value;
            }
            remove => _completed -= value;
        }

        public override bool keepWaiting => !IsDone;

        #region await

        public bool IsCompleted => IsDone;
        public AsyncOperationBase GetAwaiter() => this;

        public void OnCompleted(Action continuation)
        {
            if (IsDone)
            {
                continuation?.Invoke();
                return;
            }

            _continuation += continuation;
        }

        public void GetResult()
        {
            if (Status == EOperationStatus.Failed || Status == EOperationStatus.Canceled)
                throw new InvalidOperationException(Error ?? "Resource operation failed.");
        }

        #endregion

        #region IEnumerator

        object IEnumerator.Current => null;
        bool IEnumerator.MoveNext() => !IsDone;
        void IEnumerator.Reset() => throw new NotSupportedException();

        #endregion

        internal void Start()
        {
            if (Status != EOperationStatus.None)
                return;

            Status = EOperationStatus.Processing;
            Progress = 0f;
            try
            {
                InternalOnStart();
            }
            catch (Exception e)
            {
                SetFinish(EOperationStatus.Failed, e.ToString());
            }
        }

        internal void Update()
        {
            if (Status != EOperationStatus.Processing)
                return;

            if (CancellationRequested)
            {
                SetFinish(EOperationStatus.Canceled, "Canceled");
                return;
            }

            try
            {
                InternalOnUpdate();
            }
            catch (Exception e)
            {
                SetFinish(EOperationStatus.Failed, e.ToString());
            }
        }

        /// <summary>请求取消；下一帧或当前 Update 进入 Canceled。</summary>
        public virtual void Cancel()
        {
            if (IsDone)
                return;
            CancellationRequested = true;
            OnCancelRequested();
            if (Status == EOperationStatus.None || Status == EOperationStatus.Processing)
                SetFinish(EOperationStatus.Canceled, "Canceled");
        }

        protected virtual void OnCancelRequested()
        {
        }

        protected abstract void InternalOnStart();

        protected virtual void InternalOnUpdate()
        {
        }

        protected void SetFinish(
            EOperationStatus status,
            string error = null,
            AsyncOperationBase causedBy = null,
            bool logFail = true)
        {
            if (IsDone)
                return;

            Status = status;
            Error = error;
            if (status == EOperationStatus.Succeed)
                Progress = 1f;

            if (status == EOperationStatus.Failed && logFail)
                ZeonAssetLog.OperationFail(this, error, causedBy);

            try
            {
                OnFinished();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            var cont = _continuation;
            _continuation = null;
            cont?.Invoke();

            var completed = _completed;
            _completed = null;
            completed?.Invoke(this);
        }

        protected virtual void OnFinished()
        {
        }

        protected void Succeed() => SetFinish(EOperationStatus.Succeed);

        protected void Fail(string error) => SetFinish(EOperationStatus.Failed, error);

        /// <summary>子操作已失败：Error 用子错误，日志带 ← 链路。</summary>
        protected void FailFrom(AsyncOperationBase child)
        {
            SetFinish(EOperationStatus.Failed, child?.Error ?? "Unknown", child);
        }

        /// <summary>跟随者等场景：失败原因已由主任务打过日志。</summary>
        protected void FailSilent(string error) =>
            SetFinish(EOperationStatus.Failed, error, null, logFail: false);

        internal virtual void OnRecycle()
        {
            Status = EOperationStatus.None;
            Error = null;
            Progress = 0f;
            CancellationRequested = false;
            _continuation = null;
            _completed = null;
        }

        /// <summary>
        /// 入池。非池化 Operation 返回 false。调度器在完成后延迟一帧调用。
        /// </summary>
        internal virtual bool TryReturnToPool() => false;
    }

    public abstract class AsyncOperationBase<TResult> : AsyncOperationBase
    {
        public TResult Result { get; protected set; }

        public new TResult GetResult()
        {
            base.GetResult();
            return Result;
        }

        internal override void OnRecycle()
        {
            Result = default;
            base.OnRecycle();
        }
    }
}
