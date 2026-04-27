# BovineLabs.Spatial

Spatial partitioning and neighbor query system for Unity DOTS.

## Features

- **GatherTargetsJob**: Collects entities within camera frustum range into a `NativeList`.
- **SpatialMap**: Builds a 2D spatial hash map from active targets for O(1) bucket lookup.
- **FindNeighborsJob**: Iterates spatial hash buckets, checks distances, populates a neighbor buffer per entity.

## Requirements

- Unity 6000.0+
- com.unity.entities 6.5.0+
- com.bovinelabs.core 1.6.1+
