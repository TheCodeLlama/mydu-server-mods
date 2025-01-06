# Elevators and doors using dynamic constructs moved by server

This sample DLL Mod illustrates how to use dynamic constructs as elevators and
doors.

## Elevator

Create a space construct that must note have any rotation (rotate in backoffice
to "0 0 0 1").

Put two XS adjustors as markers of the levels you want reach.

Create a simple dynamic construct with just a core unit and a voxel platform
for player to step on.

To use, step on the elevator (so that player is parented to it), right click
anywhere pointing at the main space construct, and select "mod: Elevator".

The elevator will go to the next closest level, accelerating and decelerating,
carying the player with it.

## Secret door

Use a setup similar as the elevator. Make a dynamic construct with a core and
a voxel wall blocking a passageway (make a small hole in the ceiling to let
the dynamic construct go through.

Put two XS adjustors on the space construct to mark the two elevations of the
door core unit, close to it.

To use, point at the dynamic door construct, right click and select "Mod: Try secret door".

The door should open and close to reach the two levels.

Note: try it without boarding rights to the dynamic construct or you might run
into issues.

## Rotating door

Mod actions "Rotate open" and "Rotate close" will slowly rotate the target construc
by 90 degres, using the core position as Z axis pivot.