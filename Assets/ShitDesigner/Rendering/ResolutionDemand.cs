using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Rendering
{
    public enum ResolutionDemandKind
    {
        Program,
        FocusedPreview,
        OtherPreview
    }

    public readonly struct ResolutionDemand
    {
        public ResolutionDemandKind Kind { get; }
        public Vector2Int Size { get; }
        public long FocusOrder { get; }
        public NodeInstanceId NodeId { get; }

        public ResolutionDemand(ResolutionDemandKind kind, Vector2Int size, long focusOrder, NodeInstanceId nodeId)
        {
            if (size.x < 1 || size.y < 1) throw new ArgumentOutOfRangeException(nameof(size));
            if (kind == ResolutionDemandKind.Program && focusOrder != 0) throw new ArgumentException("Program demand does not use focus order.", nameof(focusOrder));
            Kind = kind;
            Size = size;
            FocusOrder = focusOrder;
            NodeId = nodeId;
        }
    }

    public readonly struct ResolutionDemandResult
    {
        public Vector2Int Size { get; }
        public ResolutionDemand Winner { get; }
        public float AspectRatio => Size.x / (float)Size.y;

        internal ResolutionDemandResult(Vector2Int size, ResolutionDemand winner)
        {
            Size = size;
            Winner = winner;
        }
    }

    public static class ResolutionDemandIntegrator
    {
        public static Result<ResolutionDemandResult> Merge(IEnumerable<ResolutionDemand> demands)
        {
            var list = (demands ?? Enumerable.Empty<ResolutionDemand>()).ToList();
            if (list.Count == 0)
                return Result<ResolutionDemandResult>.Failure(new Diagnostic(new DiagnosticCode("rendering.demand.empty"), Severity.Error, "At least one resolution demand is required."));
            var winner = list
                .OrderBy(demand => Priority(demand.Kind))
                .ThenByDescending(demand => demand.Kind == ResolutionDemandKind.Program ? 0 : demand.FocusOrder)
                .ThenBy(demand => demand.NodeId.Value, StringComparer.Ordinal)
                .First();
            var aspect = winner.Size.x / (double)winner.Size.y;
            var width = list.Max(demand => Math.Max(demand.Size.x, (int)Math.Ceiling(demand.Size.y * aspect)));
            var height = list.Max(demand => Math.Max(demand.Size.y, (int)Math.Ceiling(demand.Size.x / aspect)));
            width = Math.Max(width, (int)Math.Ceiling(height * aspect));
            height = Math.Max(height, (int)Math.Ceiling(width / aspect));
            return Result<ResolutionDemandResult>.Success(new ResolutionDemandResult(new Vector2Int(width, height), winner));
        }

        private static int Priority(ResolutionDemandKind kind) => kind == ResolutionDemandKind.Program ? 0 : kind == ResolutionDemandKind.FocusedPreview ? 1 : 2;
    }
}
