namespace Lasso

open System
open System.Linq


open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Git
open Aardvark.SceneGraph




[<AutoOpen>]
module Operators =
    let inline (<==) (m : gitref<'a>) (value : 'a) =
        GitRef.change m value


module Lasso =

    
    // A polygon on the near plane plus the trafo to transform points in world space onto the near plane. This represents one Lasso.
    type NearPlanePolygon = 
        {
            Polygon         : Polygon2d
            World2NearPlane : Trafo3d
            Proj            : Trafo3d
            View            : Trafo3d
            TriangleList    : V2d list
        }

    type Selection = 
        | Single      of             NearPlanePolygon
        | And         of Selection * NearPlanePolygon 
        | Or          of Selection * NearPlanePolygon
        | Xor         of Selection * NearPlanePolygon
        | Subtract    of Selection * NearPlanePolygon
        | Invert      of Selection
        | NoSelection

    type LassoMode =
        | Lasso
        | Polygon
        | Rectangle
        | NoLasso

    type Interaction =
        | SelectionStart of LassoMode
        | UpdateSelection of V2d
        | SelectionEnd 
        | ClearSelection 

    type WorkingState = { points : gitref<list<V2d>>}//; git : WorkingCopy }
     
    type InteractionState = 
        { 
            currentMode         : ref<LassoMode> 
            workingState        : WorkingState //gitref<Option<WorkingState>>
            currentSelection    : gitref<list<V2d>>
        }

    let initState (git : WorkingCopy) = 
        { 
            currentMode = ref NoLasso
            workingState = {points = git.ref "points" []}//git.ref "lasso working state" None
            currentSelection = git.ref "selection" [] 
        }
        
    module Logic =
        let resultrect (fst : V2d) (snd : V2d) =
            [|
                V2d(fst.X,  fst.Y)
                V2d(snd.X,  fst.Y)
                V2d(snd.X,  snd.Y)
                V2d(fst.X,  snd.Y)
            |]
     

        let addPoint (p : V2d) (s : WorkingState) = 
            //let change = 
                change {
                    match s.points.Value with
                    | [] ->         do! s.points <== p :: s.points.Value
                    | last::[] ->   do! s.points <== p :: s.points.Value
                    | last::_::_ -> 
                        let distance = (p-last).Length
                        match distance < 0.01 with
                        | true ->   do! Change.empty
                        | false ->  do! s.points <== p :: s.points.Value
                } 
            //s.git.apply change 
            //s.git.commit "added point" 

        let interact (s : InteractionState) interaction =
            let emptyLasso = 
                s.currentSelection <== []

            let freshWorkingState () = 
                // REVIEW gh: WTF?????
                //let freshRepo = WorkingCopy.init("")
                change { 
                    do! s.workingState.points <== [] 
                    //<== Some { git = freshRepo; points = freshRepo.ref "points" [] } 
                }
             
            //match interaction,s.workingState.Value with
            match interaction,s.workingState with
                | ClearSelection,_      -> emptyLasso
                | SelectionStart mode,_ ->
                    s.currentMode := mode
                    change { 
                        do! freshWorkingState()
                        do! emptyLasso 
                    }
                | SelectionEnd, workingState ->
                    s.currentMode := NoLasso
                    change {
                        do! s.currentSelection <== workingState.points.Value                    
                        do! freshWorkingState()
                    }
                | UpdateSelection pos, workingState ->
                    match !s.currentMode with
                        | NoLasso   ->  Change.empty
                        | _         ->  addPoint pos workingState // ; Change.empty

                //| _ -> failwithf "invalid state: %A %A" s interaction

    let doubleclick (state : InteractionState) (pos : V2d) =
        match !state.currentMode with
            | Polygon ->    Logic.interact state (SelectionEnd)
            | Lasso
            | Rectangle 
            | NoLasso ->    Change.empty

    let singleclick (state : InteractionState) (lassoMode : LassoMode) (pos : V2d) =
        match !state.currentMode with
            | NoLasso    -> 
                match lassoMode with
                 | Polygon -> 
                     change {
                        do! Logic.interact state (SelectionStart Polygon)
                        do! Logic.interact state (UpdateSelection pos)
                    }
                 | _ ->
                    Change.empty

            | Lasso ->     Change.empty
            | Polygon ->   Logic.interact state (UpdateSelection pos) 
            | Rectangle -> Change.empty

    let clickup (state : InteractionState) (pos : V2d) =
        match !state.currentMode with
            | NoLasso ->    Change.empty
            | Lasso ->      Logic.interact state (SelectionEnd) 
            | Polygon ->    Change.empty 
            | Rectangle ->
                change {
                    do! Logic.interact state (UpdateSelection pos)
                    do! Logic.interact state (SelectionEnd) 
                }

    let clickdown (state : InteractionState) (lassoMode : LassoMode) (pos : V2d) =
        match !state.currentMode with
            | NoLasso   ->             
                match lassoMode with
                    | Rectangle -> 
                        change {
                            do! Logic.interact state (SelectionStart Rectangle)
                            do! Logic.interact state (UpdateSelection pos)  
                        }

                    | Lasso     -> 
                        change {
                            do! Logic.interact state (SelectionStart Lasso)
                            do! Logic.interact state (UpdateSelection pos)  
                        }

                    | _ ->         
                        Change.empty

            | Lasso ->     Change.empty
            | Polygon ->   Change.empty 
            | Rectangle -> Change.empty


    let move (state : InteractionState) (pos : V2d) =
        match !state.currentMode with
            | Lasso ->      Logic.interact state (UpdateSelection pos)
            | NoLasso 
            | Polygon
            | Rectangle ->  Change.empty


module LassoInteraction =

    open Aardvark.Git
    open Lasso

    type Combinator =
        | And
        | Or
        | Xor
        | Subtract
        | Overwrite

    type SelectionState = { selection : gitref<Selection> }

    let processSelection (state : SelectionState) (vertices : list<V2d>) (world2NearPlane : Trafo3d) (viewTrafo : Trafo3d) (projTrafo : Trafo3d) (mode : Lasso.LassoMode) (combinator : Combinator) : Change =
        let poly = 
            let corners =
                match mode with
                    | Lasso.LassoMode.Rectangle ->
                        match vertices with
                            | [] ->         failwith "rectangle corners is empty"
                            | fst::[] ->    failwith "rectangle missing second corner"                
                            | fst::snd::_ ->Lasso.Logic.resultrect fst snd 
                    | _ ->              vertices |> List.toArray
            Polygon2d(corners)
        
        // Triangulate Polygon
        
        let gpcPoly = GPC.GpcPolygon(poly.Points.ToArray())                   
            // Create lightcap Vertex Array           
        let triangleList = 
            gpcPoly.ComputeTriangulation() 
                |> Array.map ( fun t -> [| t.P2; t.P1; t.P0 |] ) 
                |> Array.concat 
                |> Array.toList    
        
        let npp = { 
                Polygon         = poly; 
                World2NearPlane = world2NearPlane; 
                View            = viewTrafo ; 
                Proj            = projTrafo; 
                TriangleList    = triangleList
            }

        match state.selection.Value with
            | NoSelection ->
                change {
                    do! state.selection <== Single npp
                }
            | _ -> 
                match combinator with
                    | Combinator.Overwrite -> 
                        state.selection <== Single npp

                    | Combinator.And -> 
                        state.selection <== Selection.And(state.selection.Value,npp)

                    | Combinator.Or -> 
                        state.selection <== Selection.Or(state.selection.Value,npp) 

                    | Combinator.Subtract -> 
                        state.selection <== Selection.Subtract(state.selection.Value,npp) 

                    | Combinator.Xor -> 
                        state.selection <== Selection.Xor(state.selection.Value,npp) 
 
 

    let rec reportstr (s:Selection) =
        match s with
             | Selection.NoSelection  -> "| No Selection"
             | Selection.Single x     -> sprintf "| Single %s"                   (x.Polygon.PointCount.ToString())
             | Selection.Or(s,x)      -> reportstr s + sprintf "| Or %s"         (x.Polygon.PointCount.ToString())
             | Selection.And(s,x)     -> reportstr s + sprintf "| And %s"        (x.Polygon.PointCount.ToString())
             | Selection.Xor(s,x)     -> reportstr s + sprintf "| Xor %s"        (x.Polygon.PointCount.ToString())
             | Selection.Subtract(s,x)-> reportstr s + sprintf "| Subtract %s"   (x.Polygon.PointCount.ToString())
             | Selection.Invert s     -> reportstr s + "| Invert"

    let reportSelection (state : SelectionState) =
        Report.Line("Updated selection, new value: {0}",reportstr state.selection.Value)


    let select (state : SelectionState) (interactionState : Lasso.InteractionState ) (world2NearPlane  : IMod<Trafo3d>) (viewTrafo : IMod<Trafo3d>) (projTrafo : IMod<Trafo3d>) (mode : Lasso.LassoMode) (combinator : IMod<Combinator>) =
        let vertices = interactionState.currentSelection.Value
        match vertices with 
        | [] -> Change.empty
        | _ -> 
            change {
                let world2NearPlane = world2NearPlane  |> Mod.force
                let view = viewTrafo |> Mod.force
                let proj = projTrafo |> Mod.force
                let combinator = combinator |> Mod.force
                do! processSelection state vertices world2NearPlane view proj mode combinator
                do reportSelection state
            }
     
    let invert (state : SelectionState) : Change =
        change {
            do! state.selection <== Invert(state.selection.Value)
            do reportSelection state
        }

    let deselect (state : SelectionState) : Change =
        change {
            do! state.selection <== NoSelection
            do reportSelection state
        }

    let initSelection (w : WorkingCopy) =
        { selection = w.ref "SelectionState" NoSelection }


module MouseKeyboard =

    open Aardvark.Application

    
    type Modifiers =
        {
            Shift : IMod<bool>
            Alt   : IMod<bool>
            Ctrl  : IMod<bool>
        }

    type VgmLasso =
        {
            WorkingCopy     : WorkingCopy
            Selection       : LassoInteraction.SelectionState
            State           : Lasso.InteractionState
            Mode            : ref<Lasso.LassoMode>
            Combinator      : IMod<LassoInteraction.Combinator>
            View            : IMod<CameraView>
            Proj            : IMod<Frustum>
        }


    module Lasso =
        
        //let WorkingCopy = WorkingCopy.init ()

        module Commit =
            let auto (w : WorkingCopy) (c : Change) =
                w.apply c
                //w.autocommit()

            let change (w : WorkingCopy) msg (c : Change) =
                w.apply c
                w.commit msg

        module Mouse = 
            let viewTrafo (s : VgmLasso) = s.View |> Mod.force |> CameraView.viewTrafo
            let projTrafo (s : VgmLasso) = s.Proj |> Mod.force |> Frustum.projTrafo
            

            let currentTrafo (s : VgmLasso) =
                let view = s.View |> Mod.force |> CameraView.viewTrafo
                let proj = s.Proj |> Mod.force |> Frustum.projTrafo
                view * proj

            let double (s : VgmLasso) (pos : V2d) = 
                Lasso.doubleclick s.State pos 
                    |> Commit.auto s.WorkingCopy

            let single (s : VgmLasso) (pos : V2d) =
                Lasso.singleclick s.State !s.Mode pos 
                    |> Commit.auto s.WorkingCopy
  
                let trafo = currentTrafo s
                let view = viewTrafo s
                let proj = projTrafo s

                LassoInteraction.select s.Selection s.State (Mod.constant trafo) (Mod.constant view) (Mod.constant proj) !s.Mode s.Combinator
                    //|> Commit.auto WorkingCopy
                    |> Commit.change s.WorkingCopy ""

            let mouseup (s : VgmLasso) (pos : V2d) =
                Lasso.clickup s.State pos
                    |> Commit.auto s.WorkingCopy
                    
                let trafo = currentTrafo s
                let view = viewTrafo s
                let proj = projTrafo s
                LassoInteraction.select s.Selection s.State (Mod.constant trafo) (Mod.constant view) (Mod.constant proj) !s.Mode s.Combinator
                    |> Commit.auto s.WorkingCopy

            let mousedown (s : VgmLasso) (pos : V2d) = 
                Lasso.clickdown s.State !s.Mode pos
                    |> Commit.auto s.WorkingCopy

            let move (s : VgmLasso) (pos : V2d) =
                Lasso.move s.State pos
                    |> Commit.auto s.WorkingCopy
//                let changes = Lasso.move s.State pos
//                if not (Change.isEmpty changes) then
//                    failwith "Mouse move should not have produced changes"

        module Keyboard =
            let switchToLasso (s : VgmLasso)        = s.Mode := Lasso.LassoMode.Lasso       ; Report.Line "Switched to Lasso"
            let switchToPolygon (s : VgmLasso)      = s.Mode := Lasso.LassoMode.Polygon     ; Report.Line "Switched to Polygon"
            let switchToRectangle (s : VgmLasso)    = s.Mode := Lasso.LassoMode.Rectangle   ; Report.Line "Switched to Rectangle"
            let invertSelection (s : VgmLasso)      = LassoInteraction.invert s.Selection   |> Commit.change s.WorkingCopy "lasso invert"
            let deselect (s: VgmLasso)              = LassoInteraction.deselect s.Selection |> Commit.auto s.WorkingCopy
                            
    let initKeyboard (s : VgmLasso) (rc : IRenderControl) = 
        rc.Keyboard.Down.Values.Add (
            fun key ->
                match key with
                | Keys.L ->
                    Lasso.Keyboard.switchToLasso s
                | Keys.P ->
                    Lasso.Keyboard.switchToPolygon s
                | Keys.R ->
                    Lasso.Keyboard.switchToRectangle s
                | Keys.I -> 
                    Lasso.Keyboard.invertSelection s
                | _ -> ()
        )

    let initMouse (enabled : IMod<bool>) (s : VgmLasso) (rc : IRenderControl) = 

        rc.Mouse.DoubleClick.Values.Add(
            fun b ->
                if Mod.force enabled then
                    let pos = rc.Mouse.Position |> Mod.force
                    
                    match b with
                    | MouseButtons.Left -> 
                        Lasso.Mouse.double s pos.NormalizedPosition
                        ()
                    | _ -> ()
            )

        rc.Mouse.Click.Values.Add(
            fun b ->
                if Mod.force enabled then
                    let pos = rc.Mouse.Position |> Mod.force
                    match b with
                    | MouseButtons.Left -> 
                        Lasso.Mouse.single s pos.NormalizedPosition
                    | _ -> ()
            )

        rc.Mouse.Down.Values.Add(
            fun b ->
                if Mod.force enabled then
                    let pos = rc.Mouse.Position |> Mod.force
                    match b with
                    | MouseButtons.Left -> 
                        Lasso.Mouse.mousedown s pos.NormalizedPosition
                    | _ -> ()
            )

        rc.Mouse.Up.Values.Add(
            fun b ->
                if Mod.force enabled then
                    let pos = rc.Mouse.Position |> Mod.force
                    match b with
                    | MouseButtons.Left -> 
                        Lasso.Mouse.mouseup s pos.NormalizedPosition
                    | _ -> ()
            )

        rc.Mouse.Move.Values.Add (
            fun (oldpos, newpos) -> 
                if Mod.force enabled then
                    Lasso.Mouse.move s newpos.NormalizedPosition
            )


module State =
    open Aardvark.Application
    open MouseKeyboard

    let initModifiers (control : IRenderControl) =
        {
            Shift = control.Keyboard.IsDown Keys.LeftShift
            Alt   = control.Keyboard.IsDown Keys.LeftAlt
            Ctrl  = control.Keyboard.IsDown Keys.LeftCtrl
        }

    let initLasso view proj (modifiers : Modifiers) (workingCopy : WorkingCopy) =
        {
            WorkingCopy = workingCopy
            Selection   = LassoInteraction.initSelection workingCopy
            State       = Lasso.initState workingCopy
            Mode        = ref Lasso.LassoMode.Lasso
            View        = view
            Proj        = proj
            Combinator  = 
                adaptive {
                    let! shift = modifiers.Shift
                    let! alt   = modifiers.Alt
                    let! ctrl  = modifiers.Ctrl
                    match shift,alt,ctrl with
                        | false,false,false -> return LassoInteraction.Combinator.Overwrite
                        | true, false,_     -> return LassoInteraction.Combinator.Or
                        | true, true, _     -> return LassoInteraction.Combinator.And
                        | false,true, _     -> return LassoInteraction.Combinator.Subtract
                        | _,    _,    true  -> return LassoInteraction.Combinator.Xor
                }
        }


module LassoSg =
    
    open Aardvark.Application
    open Aardvark.SceneGraph


        

    // create vertex-data using the latest preview point-list
    let visualization (v : IMod<V2d[]>) (rc : IRenderControl) =
        let verts = adaptive {
            let! l = v
            let points =
                l |> Array.map (fun n -> 
                    V3f(2.0 * n.X - 1.0, 1.0 - 2.0 * n.Y, 0.0)
                )
            if points |> Array.length > 0 then
                return Array.append points [| points.[0] |]
            else
                return points
        }

        // create an sg representing the current preview
        Sg.draw IndexedGeometryMode.LineStrip
            |> Sg.vertexAttribute DefaultSemantic.Positions verts
            |> Sg.uniform "LineWidth" (Mod.init 5.0)
            |> Sg.uniform "ViewportSize" rc.Sizes

    let withSg (s : ISg) (rc : IRenderControl) (l : MouseKeyboard.VgmLasso) =
        let previewOfLasso =
            let rect (mouse : V2d) (corner : V2d) =
                [|
                    V2d(corner.X, corner.Y)
                    V2d(mouse.X,  corner.Y)
                    V2d(mouse.X,  mouse.Y )
                    V2d(corner.X, mouse.Y )
                    V2d(corner.X, corner.Y)
                |]
            adaptive {
                let! mouse = rc.Mouse.Position
                let mp = mouse.NormalizedPosition
                let! v = l.State.workingState.points
                match !l.Mode with
                    | Lasso.LassoMode.Rectangle -> return! l.State.workingState.points |> Mod.map ( fun ps -> match ps with [] -> [||] | _ -> rect mp (ps |> List.toArray).[0] )
                    | _                         -> return! l.State.workingState.points |> Mod.map ( fun ps -> mp :: ps) |> Mod.map List.toArray
                }

        s |> Sg.andAlso (visualization previewOfLasso rc)