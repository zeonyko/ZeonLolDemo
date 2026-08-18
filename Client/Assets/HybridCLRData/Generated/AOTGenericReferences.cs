using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"Game.ZeonAsset.dll",
		"System.Core.dll",
		"UnityEngine.CoreModule.dll",
		"UnityEngine.JSONSerializeModule.dll",
		"mscorlib.dll",
	};
	// }}

	// {{ constraint implement type
	// }} 

	// {{ AOT generic types
	// Game.ZeonAsset.AsyncOperationBase<object>
	// System.Action<Client.Battle.AttrChange>
	// System.Action<Client.Battle.CastTimeWindow>
	// System.Action<Client.Battle.PlaybackEvent>
	// System.Action<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Action<Client.Battle.SkillInputBuffer.Entry>
	// System.Action<Client.Battle.TransformSnapshot>
	// System.Action<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Action<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Action<Shared.BlockerAabb>
	// System.Action<Shared.EntityStateMachine.TimedRemove>
	// System.Action<Shared.MovementReplayVerify.Pending>
	// System.Action<System.ValueTuple<int,object>>
	// System.Action<System.ValueTuple<object,object>>
	// System.Action<System.ValueTuple<uint,float>>
	// System.Action<UnityEngine.Vector3>
	// System.Action<byte,float>
	// System.Action<byte>
	// System.Action<int,byte>
	// System.Action<int,object>
	// System.Action<int>
	// System.Action<long>
	// System.Action<object,object>
	// System.Action<object>
	// System.Collections.Concurrent.ConcurrentQueue.<Enumerate>d__28<Client.Network.NetworkDelaySimulator.RawInbound>
	// System.Collections.Concurrent.ConcurrentQueue.Segment<Client.Network.NetworkDelaySimulator.RawInbound>
	// System.Collections.Concurrent.ConcurrentQueue<Client.Network.NetworkDelaySimulator.RawInbound>
	// System.Collections.Generic.ArraySortHelper<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.ArraySortHelper<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.ArraySortHelper<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.ArraySortHelper<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.ArraySortHelper<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.ArraySortHelper<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.ArraySortHelper<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.ArraySortHelper<Shared.BlockerAabb>
	// System.Collections.Generic.ArraySortHelper<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.ArraySortHelper<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.ArraySortHelper<System.ValueTuple<int,object>>
	// System.Collections.Generic.ArraySortHelper<System.ValueTuple<object,object>>
	// System.Collections.Generic.ArraySortHelper<System.ValueTuple<uint,float>>
	// System.Collections.Generic.ArraySortHelper<byte>
	// System.Collections.Generic.ArraySortHelper<int>
	// System.Collections.Generic.ArraySortHelper<long>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.Comparer<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.Comparer<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.Comparer<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.Comparer<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.Comparer<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.Comparer<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.Comparer<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.Comparer<Shared.BlockerAabb>
	// System.Collections.Generic.Comparer<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.Comparer<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.Comparer<System.ValueTuple<int,object>>
	// System.Collections.Generic.Comparer<System.ValueTuple<object,object>>
	// System.Collections.Generic.Comparer<System.ValueTuple<uint,float>>
	// System.Collections.Generic.Comparer<byte>
	// System.Collections.Generic.Comparer<float>
	// System.Collections.Generic.Comparer<int>
	// System.Collections.Generic.Comparer<long>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.Comparer<uint>
	// System.Collections.Generic.Dictionary.Enumerator<System.ValueTuple<int,object>,object>
	// System.Collections.Generic.Dictionary.Enumerator<UnityEngine.Vector3Int,int>
	// System.Collections.Generic.Dictionary.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.Enumerator<int,int>
	// System.Collections.Generic.Dictionary.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<System.ValueTuple<int,object>,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<UnityEngine.Vector3Int,int>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,int>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection<System.ValueTuple<int,object>,object>
	// System.Collections.Generic.Dictionary.KeyCollection<UnityEngine.Vector3Int,int>
	// System.Collections.Generic.Dictionary.KeyCollection<int,float>
	// System.Collections.Generic.Dictionary.KeyCollection<int,int>
	// System.Collections.Generic.Dictionary.KeyCollection<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection<long,object>
	// System.Collections.Generic.Dictionary.KeyCollection<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<System.ValueTuple<int,object>,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<UnityEngine.Vector3Int,int>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,int>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection<System.ValueTuple<int,object>,object>
	// System.Collections.Generic.Dictionary.ValueCollection<UnityEngine.Vector3Int,int>
	// System.Collections.Generic.Dictionary.ValueCollection<int,float>
	// System.Collections.Generic.Dictionary.ValueCollection<int,int>
	// System.Collections.Generic.Dictionary.ValueCollection<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection<long,object>
	// System.Collections.Generic.Dictionary.ValueCollection<object,object>
	// System.Collections.Generic.Dictionary<System.ValueTuple<int,object>,object>
	// System.Collections.Generic.Dictionary<UnityEngine.Vector3Int,int>
	// System.Collections.Generic.Dictionary<int,float>
	// System.Collections.Generic.Dictionary<int,int>
	// System.Collections.Generic.Dictionary<int,object>
	// System.Collections.Generic.Dictionary<long,object>
	// System.Collections.Generic.Dictionary<object,object>
	// System.Collections.Generic.EqualityComparer<System.ValueTuple<int,object>>
	// System.Collections.Generic.EqualityComparer<UnityEngine.Vector3Int>
	// System.Collections.Generic.EqualityComparer<float>
	// System.Collections.Generic.EqualityComparer<int>
	// System.Collections.Generic.EqualityComparer<long>
	// System.Collections.Generic.EqualityComparer<object>
	// System.Collections.Generic.EqualityComparer<uint>
	// System.Collections.Generic.HashSet.Enumerator<int>
	// System.Collections.Generic.HashSet.Enumerator<long>
	// System.Collections.Generic.HashSet<int>
	// System.Collections.Generic.HashSet<long>
	// System.Collections.Generic.HashSetEqualityComparer<int>
	// System.Collections.Generic.HashSetEqualityComparer<long>
	// System.Collections.Generic.ICollection<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.ICollection<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.ICollection<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.ICollection<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.ICollection<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.ICollection<Client.Network.NetworkDelaySimulator.RawInbound>
	// System.Collections.Generic.ICollection<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.ICollection<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.ICollection<Shared.BlockerAabb>
	// System.Collections.Generic.ICollection<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.ICollection<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<System.ValueTuple<int,object>,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<UnityEngine.Vector3Int,int>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,int>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ICollection<System.ValueTuple<int,object>>
	// System.Collections.Generic.ICollection<System.ValueTuple<object,object>>
	// System.Collections.Generic.ICollection<System.ValueTuple<uint,float>>
	// System.Collections.Generic.ICollection<byte>
	// System.Collections.Generic.ICollection<int>
	// System.Collections.Generic.ICollection<long>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.IComparer<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.IComparer<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.IComparer<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.IComparer<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.IComparer<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.IComparer<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.IComparer<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.IComparer<Shared.BlockerAabb>
	// System.Collections.Generic.IComparer<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.IComparer<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.IComparer<System.ValueTuple<int,object>>
	// System.Collections.Generic.IComparer<System.ValueTuple<object,object>>
	// System.Collections.Generic.IComparer<System.ValueTuple<uint,float>>
	// System.Collections.Generic.IComparer<byte>
	// System.Collections.Generic.IComparer<int>
	// System.Collections.Generic.IComparer<long>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IEnumerable<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.IEnumerable<Client.Battle.PendingPrediction>
	// System.Collections.Generic.IEnumerable<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.IEnumerable<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.IEnumerable<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.IEnumerable<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.IEnumerable<Client.Network.NetworkDelaySimulator.RawInbound>
	// System.Collections.Generic.IEnumerable<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.IEnumerable<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.IEnumerable<Shared.BlockerAabb>
	// System.Collections.Generic.IEnumerable<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.IEnumerable<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<System.ValueTuple<int,object>,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<UnityEngine.Vector3Int,int>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,int>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerable<System.ValueTuple<int,object>>
	// System.Collections.Generic.IEnumerable<System.ValueTuple<object,object>>
	// System.Collections.Generic.IEnumerable<System.ValueTuple<uint,float>>
	// System.Collections.Generic.IEnumerable<byte>
	// System.Collections.Generic.IEnumerable<int>
	// System.Collections.Generic.IEnumerable<long>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerator<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.IEnumerator<Client.Battle.PendingPrediction>
	// System.Collections.Generic.IEnumerator<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.IEnumerator<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.IEnumerator<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.IEnumerator<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.IEnumerator<Client.Network.NetworkDelaySimulator.RawInbound>
	// System.Collections.Generic.IEnumerator<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.IEnumerator<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.IEnumerator<Shared.BlockerAabb>
	// System.Collections.Generic.IEnumerator<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.IEnumerator<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<System.ValueTuple<int,object>,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<UnityEngine.Vector3Int,int>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,int>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerator<System.ValueTuple<int,object>>
	// System.Collections.Generic.IEnumerator<System.ValueTuple<object,object>>
	// System.Collections.Generic.IEnumerator<System.ValueTuple<uint,float>>
	// System.Collections.Generic.IEnumerator<byte>
	// System.Collections.Generic.IEnumerator<int>
	// System.Collections.Generic.IEnumerator<long>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IEqualityComparer<System.ValueTuple<int,object>>
	// System.Collections.Generic.IEqualityComparer<UnityEngine.Vector3Int>
	// System.Collections.Generic.IEqualityComparer<int>
	// System.Collections.Generic.IEqualityComparer<long>
	// System.Collections.Generic.IEqualityComparer<object>
	// System.Collections.Generic.IList<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.IList<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.IList<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.IList<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.IList<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.IList<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.IList<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.IList<Shared.BlockerAabb>
	// System.Collections.Generic.IList<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.IList<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.IList<System.ValueTuple<int,object>>
	// System.Collections.Generic.IList<System.ValueTuple<object,object>>
	// System.Collections.Generic.IList<System.ValueTuple<uint,float>>
	// System.Collections.Generic.IList<byte>
	// System.Collections.Generic.IList<int>
	// System.Collections.Generic.IList<long>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.IReadOnlyCollection<Shared.BlockerAabb>
	// System.Collections.Generic.IReadOnlyCollection<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IReadOnlyCollection<int>
	// System.Collections.Generic.IReadOnlyCollection<object>
	// System.Collections.Generic.IReadOnlyDictionary<long,object>
	// System.Collections.Generic.IReadOnlyList<int>
	// System.Collections.Generic.IReadOnlyList<object>
	// System.Collections.Generic.KeyValuePair<System.ValueTuple<int,object>,object>
	// System.Collections.Generic.KeyValuePair<UnityEngine.Vector3Int,int>
	// System.Collections.Generic.KeyValuePair<int,float>
	// System.Collections.Generic.KeyValuePair<int,int>
	// System.Collections.Generic.KeyValuePair<int,object>
	// System.Collections.Generic.KeyValuePair<long,object>
	// System.Collections.Generic.KeyValuePair<object,object>
	// System.Collections.Generic.List.Enumerator<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.List.Enumerator<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.List.Enumerator<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.List.Enumerator<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.List.Enumerator<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.List.Enumerator<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.List.Enumerator<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.List.Enumerator<Shared.BlockerAabb>
	// System.Collections.Generic.List.Enumerator<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.List.Enumerator<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.List.Enumerator<System.ValueTuple<int,object>>
	// System.Collections.Generic.List.Enumerator<System.ValueTuple<object,object>>
	// System.Collections.Generic.List.Enumerator<System.ValueTuple<uint,float>>
	// System.Collections.Generic.List.Enumerator<byte>
	// System.Collections.Generic.List.Enumerator<int>
	// System.Collections.Generic.List.Enumerator<long>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.List<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.List<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.List<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.List<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.List<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.List<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.List<Shared.BlockerAabb>
	// System.Collections.Generic.List<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.List<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.List<System.ValueTuple<int,object>>
	// System.Collections.Generic.List<System.ValueTuple<object,object>>
	// System.Collections.Generic.List<System.ValueTuple<uint,float>>
	// System.Collections.Generic.List<byte>
	// System.Collections.Generic.List<int>
	// System.Collections.Generic.List<long>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.ObjectComparer<Client.Battle.CastTimeWindow>
	// System.Collections.Generic.ObjectComparer<Client.Battle.PlaybackEvent>
	// System.Collections.Generic.ObjectComparer<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.Generic.ObjectComparer<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.Generic.ObjectComparer<Client.Battle.TransformSnapshot>
	// System.Collections.Generic.ObjectComparer<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.Generic.ObjectComparer<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.Generic.ObjectComparer<Shared.BlockerAabb>
	// System.Collections.Generic.ObjectComparer<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.Generic.ObjectComparer<Shared.MovementReplayVerify.Pending>
	// System.Collections.Generic.ObjectComparer<System.ValueTuple<int,object>>
	// System.Collections.Generic.ObjectComparer<System.ValueTuple<object,object>>
	// System.Collections.Generic.ObjectComparer<System.ValueTuple<uint,float>>
	// System.Collections.Generic.ObjectComparer<byte>
	// System.Collections.Generic.ObjectComparer<float>
	// System.Collections.Generic.ObjectComparer<int>
	// System.Collections.Generic.ObjectComparer<long>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.Generic.ObjectComparer<uint>
	// System.Collections.Generic.ObjectEqualityComparer<System.ValueTuple<int,object>>
	// System.Collections.Generic.ObjectEqualityComparer<UnityEngine.Vector3Int>
	// System.Collections.Generic.ObjectEqualityComparer<float>
	// System.Collections.Generic.ObjectEqualityComparer<int>
	// System.Collections.Generic.ObjectEqualityComparer<long>
	// System.Collections.Generic.ObjectEqualityComparer<object>
	// System.Collections.Generic.ObjectEqualityComparer<uint>
	// System.Collections.Generic.Queue.Enumerator<Client.Battle.InputPlayerComponent.SkillPress>
	// System.Collections.Generic.Queue.Enumerator<Client.Battle.PendingPrediction>
	// System.Collections.Generic.Queue.Enumerator<Client.Network.Packet>
	// System.Collections.Generic.Queue<Client.Battle.InputPlayerComponent.SkillPress>
	// System.Collections.Generic.Queue<Client.Battle.PendingPrediction>
	// System.Collections.Generic.Queue<Client.Network.Packet>
	// System.Collections.Generic.Stack.Enumerator<object>
	// System.Collections.Generic.Stack<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<Client.Battle.CastTimeWindow>
	// System.Collections.ObjectModel.ReadOnlyCollection<Client.Battle.PlaybackEvent>
	// System.Collections.ObjectModel.ReadOnlyCollection<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Collections.ObjectModel.ReadOnlyCollection<Client.Battle.SkillInputBuffer.Entry>
	// System.Collections.ObjectModel.ReadOnlyCollection<Client.Battle.TransformSnapshot>
	// System.Collections.ObjectModel.ReadOnlyCollection<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Collections.ObjectModel.ReadOnlyCollection<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Collections.ObjectModel.ReadOnlyCollection<Shared.BlockerAabb>
	// System.Collections.ObjectModel.ReadOnlyCollection<Shared.EntityStateMachine.TimedRemove>
	// System.Collections.ObjectModel.ReadOnlyCollection<Shared.MovementReplayVerify.Pending>
	// System.Collections.ObjectModel.ReadOnlyCollection<System.ValueTuple<int,object>>
	// System.Collections.ObjectModel.ReadOnlyCollection<System.ValueTuple<object,object>>
	// System.Collections.ObjectModel.ReadOnlyCollection<System.ValueTuple<uint,float>>
	// System.Collections.ObjectModel.ReadOnlyCollection<byte>
	// System.Collections.ObjectModel.ReadOnlyCollection<int>
	// System.Collections.ObjectModel.ReadOnlyCollection<long>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Comparison<Client.Battle.CastTimeWindow>
	// System.Comparison<Client.Battle.PlaybackEvent>
	// System.Comparison<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Comparison<Client.Battle.SkillInputBuffer.Entry>
	// System.Comparison<Client.Battle.TransformSnapshot>
	// System.Comparison<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Comparison<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Comparison<Shared.BlockerAabb>
	// System.Comparison<Shared.EntityStateMachine.TimedRemove>
	// System.Comparison<Shared.MovementReplayVerify.Pending>
	// System.Comparison<System.ValueTuple<int,object>>
	// System.Comparison<System.ValueTuple<object,object>>
	// System.Comparison<System.ValueTuple<uint,float>>
	// System.Comparison<byte>
	// System.Comparison<int>
	// System.Comparison<long>
	// System.Comparison<object>
	// System.Func<byte>
	// System.Func<object,byte>
	// System.Func<object,int>
	// System.Func<object>
	// System.Nullable<UnityEngine.Color>
	// System.Nullable<UnityEngine.Vector3>
	// System.Nullable<byte>
	// System.Nullable<float>
	// System.Predicate<Client.Battle.CastTimeWindow>
	// System.Predicate<Client.Battle.PlaybackEvent>
	// System.Predicate<Client.Battle.SkillAimAreaView.ActiveRing>
	// System.Predicate<Client.Battle.SkillInputBuffer.Entry>
	// System.Predicate<Client.Battle.TransformSnapshot>
	// System.Predicate<Client.Network.NetworkDelaySimulator.TimedInbound>
	// System.Predicate<Client.Network.NetworkDelaySimulator.TimedOutbound>
	// System.Predicate<Shared.BlockerAabb>
	// System.Predicate<Shared.EntityStateMachine.TimedRemove>
	// System.Predicate<Shared.MovementReplayVerify.Pending>
	// System.Predicate<System.ValueTuple<int,object>>
	// System.Predicate<System.ValueTuple<object,object>>
	// System.Predicate<System.ValueTuple<uint,float>>
	// System.Predicate<byte>
	// System.Predicate<int>
	// System.Predicate<long>
	// System.Predicate<object>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<byte>
	// System.Runtime.CompilerServices.TaskAwaiter<byte>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<byte>
	// System.Threading.Tasks.Task<byte>
	// System.Threading.Tasks.TaskFactory<byte>
	// System.ValueTuple<int,object>
	// System.ValueTuple<object,object>
	// System.ValueTuple<uint,float>
	// }}

	public void RefMethods()
	{
		// object System.Activator.CreateInstance<object>()
		// Client.Battle.BattlePanel.SkillSlot[] System.Array.Empty<Client.Battle.BattlePanel.SkillSlot>()
		// Client.Battle.CastTimeWindow[] System.Array.Empty<Client.Battle.CastTimeWindow>()
		// Client.Battle.SkillHotkeyTable.Entry[] System.Array.Empty<Client.Battle.SkillHotkeyTable.Entry>()
		// int[] System.Array.Empty<int>()
		// object[] System.Array.Empty<object>()
		// System.Void System.Array.Sort<object>(object[],System.Comparison<object>)
		// bool System.Enum.TryParse<int>(string,bool,int&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,object>(System.Runtime.CompilerServices.TaskAwaiter&,object&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.Start<object>(object&)
		// object& System.Runtime.CompilerServices.Unsafe.As<object,object>(object&)
		// System.Void* System.Runtime.CompilerServices.Unsafe.AsPointer<object>(object&)
		// object UnityEngine.Component.GetComponent<object>()
		// object UnityEngine.Component.GetComponentInChildren<object>()
		// object UnityEngine.Component.GetComponentInParent<object>()
		// object[] UnityEngine.Component.GetComponentsInChildren<object>(bool)
		// object UnityEngine.GameObject.AddComponent<object>()
		// object UnityEngine.GameObject.GetComponent<object>()
		// object UnityEngine.GameObject.GetComponentInChildren<object>()
		// object UnityEngine.GameObject.GetComponentInChildren<object>(bool)
		// object[] UnityEngine.GameObject.GetComponentsInChildren<object>()
		// object[] UnityEngine.GameObject.GetComponentsInChildren<object>(bool)
		// object UnityEngine.JsonUtility.FromJson<object>(string)
		// object UnityEngine.Object.FindObjectOfType<object>()
		// object UnityEngine.Object.Instantiate<object>(object)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform,bool)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Vector3,UnityEngine.Quaternion,UnityEngine.Transform)
		// object UnityEngine.Resources.GetBuiltinResource<object>(string)
	}
}