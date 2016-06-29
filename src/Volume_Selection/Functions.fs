namespace functions

open System.Collections.Generic
open System.Linq
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Rendering.NanoVg
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.WinForms
open Aardvark.Base.Rendering

open CameraController

open ShadowVolumeShader

open Aardvark.Git
 
open VolumeSelection

module Functions =
    let isFrontFacing (v : V2d[]) : bool = 
        true
        