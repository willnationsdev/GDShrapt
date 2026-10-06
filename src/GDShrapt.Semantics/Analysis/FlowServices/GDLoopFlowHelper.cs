using System;
using System.Collections.Generic;
using GDShrapt.Abstractions;

namespace GDShrapt.Semantics;

/// <summary>
/// Static helper methods for loop flow analysis.
/// Provides loop-state merging and iterator type inference.
/// </summary>
internal static class GDLoopFlowHelper
{
    /// <summary>
    /// Merges normal and continue paths with the pre-loop state for the next loop entry.
    /// </summary>
    public static GDFlowState MergeLoopBackEdge(
        GDFlowState preLoopState,
        GDFlowState loopBodyState,
        IEnumerable<GDFlowState> continueStates)
    {
        var merged = preLoopState;
        if (!loopBodyState.IsTerminated)
            merged = GDFlowState.MergeBranches(loopBodyState, merged, preLoopState);

        foreach (var continueState in continueStates)
            merged = MergeContinuingState(merged, continueState, preLoopState);

        return merged;
    }

    /// <summary>
    /// Merges all possible loop exit paths: zero iterations, normal completion, break, and continue-to-condition paths.
    /// </summary>
    public static GDFlowState MergeLoopExit(
        GDFlowState preLoopState,
        GDFlowState loopBodyState,
        IEnumerable<GDFlowState> breakStates,
        IEnumerable<GDFlowState> continueStates)
    {
        var merged = preLoopState;
        if (!loopBodyState.IsTerminated)
            merged = GDFlowState.MergeBranches(loopBodyState, merged, preLoopState);

        foreach (var breakState in breakStates)
            merged = MergeContinuingState(merged, breakState, preLoopState);

        foreach (var continueState in continueStates)
            merged = MergeContinuingState(merged, continueState, preLoopState);

        return merged;
    }

    private static GDFlowState MergeContinuingState(
        GDFlowState merged,
        GDFlowState branchState,
        GDFlowState parentState)
    {
        var continuingState = branchState.Clone();
        continuingState.IsTerminated = false;
        continuingState.Termination = null;
        return GDFlowState.MergeBranches(continuingState, merged, parentState);
    }

    /// <summary>
    /// Infers the element type from a collection type for for-loop iteration.
    /// </summary>
    public static string? InferIteratorElementType(string? collectionType, Func<string, bool>? isEnumType = null)
    {
        if (string.IsNullOrEmpty(collectionType))
            return "Variant";

        // Handle typed arrays: Array[Type] -> Type
        var collSemType = GDSemanticType.FromRuntimeTypeName(collectionType);
        if (collSemType is GDContainerSemanticType { IsArray: true } arrayCt)
            return arrayCt.ElementType.DisplayName;

        // Handle range() -> int
        if (collectionType == GDWellKnownTypes.Other.Range || collectionType == GDWellKnownTypes.Numeric.Int)
            return GDWellKnownTypes.Numeric.Int;

        // Handle String -> String (iterating chars)
        if (collectionType == GDWellKnownTypes.Strings.String)
            return GDWellKnownTypes.Strings.String;

        // Handle Dictionary -> key type (iterating keys)
        if (collSemType.IsDictionary)
        {
            var dictKeyType = collSemType is GDContainerSemanticType { IsDictionary: true } dictCt ? dictCt.KeyType?.DisplayName : null;
            return dictKeyType ?? GDWellKnownTypes.Variant;
        }

        // Handle PackedArray types
        var packedElement = GDPackedArrayTypes.GetElementType(collectionType);
        if (packedElement != null)
            return packedElement;

        // Handle enum types: for x in EnumType -> String (iterates key names)
        if (isEnumType != null && isEnumType(collectionType))
            return GDWellKnownTypes.Strings.String;

        return GDWellKnownTypes.Variant;
    }
}
