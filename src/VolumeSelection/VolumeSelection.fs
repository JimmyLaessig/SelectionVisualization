namespace VolumeSelection

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Rendering.NanoVg
open Aardvark.SceneGraph
open Aardvark.Base.Rendering

open System
open System.Linq


open ShadowVolumeShader
open Lasso

module VolumeSelection =

    type Selection =
    | Single
    | And
    | Or
    | Xor
    | Subtract
    
    let writeStencilBuffer = Set.singleton DefaultSemantic.Stencil
    let writeColorBuffer = Set.ofList[DefaultSemantic.Colors; DefaultSemantic.Stencil]




    /// <summary>
    /// Initializes the Volume Selection technique. It receives a sceneGraph with a scene appended and the last used renderpass of the geometry and performs a selection on the scene. 
    /// The function uses IMods for all parameters that can change over time to adapt its behaviour accordingly. 
    /// The function returns the modified scenegraph with the selection volumes attached and its' last used renderpass.  
    /// <param name="sceneGraph"> The scenegraph containing the geometry</param>
    /// <param name="view">The cameras view transform</param>
    /// <param name="proj">The cameras projection transform</param>
    /// <param name="lasso">The lasso to perform selection with</param>
    /// <param name="selectionColor">The color of the selected pixel</param>
    /// <param name="selectionDistance"> The distance of a selection</param>
    /// <param name="volumeColor">The color of the volume, if rendered</param>
    /// <param name="showVolumes">Indicates if the volumes should be rendered visible</param>
    /// <param name="geometryPass">The last renderpass with with the geometry is rendered</param>
    /// <param name="runtime"></param>
    /// <param name="framebufferSignature"></param>
    /// </summary>
    let Init (sceneGraph: ISg)(view: IMod<Trafo3d>)(proj: IMod<Trafo3d>)(lasso : MouseKeyboard.VgmLasso)(selectionColor: IMod<C4f>)(selectionDistance: IMod<double>)(volumeColor: IMod<C4f>)(showVolumes : IMod<bool>)(geometryPass: RenderPass)(runtime : IRuntime)(framebufferSignature: IFramebufferSignature) : (IMod<ISg> * RenderPass) = 
      
        
        let highlightSelectionPass = Rendering.RenderPass.after "Highlight_SelectionPass" Rendering.RenderPassOrder.Arbitrary geometryPass              
        
        let numRenderPasses = 100
        let numRenderPassesResize = 10
        // Allocate a number of renderPasses
        let rec appendRenderPass (afterPasses: list<RenderPass>): (list<RenderPass>) = 
            
             if List.length(afterPasses) >= numRenderPasses then
                afterPasses
             else
                match afterPasses with
                | [] -> failwith ""
                | first::_ ->
                    let renderPass = Rendering.RenderPass.after "" RenderPassOrder.Arbitrary first           
                    appendRenderPass (renderPass::afterPasses)
        
        //let resizeRenderPass(renderPassList): (list<RenderPass>)
            

        let renderPassList = appendRenderPass [RenderPass.after "" Rendering.RenderPassOrder.Arbitrary geometryPass] |> List.rev
      

        let invVolumeColor = volumeColor |> Mod.map (fun c-> (C4f(1.0f - c.R, 1.0f - c.G, 1.0f - c.B, c.A)))
        let highlightColor2 = C4f(0.0, 1.0,0.0,0.2) |> Mod.constant
        
        // Initialize Shading Effects
        let volumeAdditiveEffect    = runtime.PrepareEffect(framebufferSignature, [ShadowVolumeShader.trafo |> toEffect;  ShadowVolumeShader.ExtrudeCaps |> toEffect; DefaultSurfaces.uniformColor volumeColor  |> toEffect]) :> ISurface
        let volumeSubtractiveEffect = runtime.PrepareEffect(framebufferSignature, [ShadowVolumeShader.trafo |> toEffect;  ShadowVolumeShader.ExtrudeCaps |> toEffect; DefaultSurfaces.uniformColor invVolumeColor  |> toEffect]) :> ISurface
                          
        let normalizeStencilEffect = runtime.PrepareEffect(framebufferSignature, [DefaultSurfaces.constantColor C4f.Black |> toEffect]) :> ISurface         
        let hightlightEffect        = runtime.PrepareEffect(framebufferSignature, [DefaultSurfaces.uniformColor selectionColor |> toEffect]) :> ISurface               
        
        // Full Screen Quad Geometry
        let fullscreenQuad = IndexedGeometry (
                                Mode = IndexedGeometryMode.TriangleList, IndexArray = [| 0; 1; 2; 0; 2; 3 |],
                                       IndexedAttributes = SymDict.ofList [DefaultSemantic.Positions, [| V3f(-1.0, -1.0, 1.0); V3f(1.0, -1.0, 1.0); V3f(1.0, 1.0, 1.0); V3f(-1.0, 1.0, 1.0) |] :> Array])                         
        

        // ----------------------------------------------------------------------------------------------- //
        // Adds a Selection polygon
        // ----------------------------------------------------------------------------------------------- //
        let addSelectionPolygon(polygon : Lasso.NearPlanePolygon)(sceneGraph : ISg)(renderVolumes: bool)(renderPassList)(selection: Selection): (ISg * list<RenderPass>) =
           
            match renderPassList with
                | []                                        -> failwith "To many renderpasses used! Stop selecting you moron" // TODO RESIZE
                | renderPassVolume::renderPassNormalize::remainingPasses -> 

                    let worldTrafo = (polygon.View * polygon.Proj).Inverse |> Mod.constant  // Transformation into world space 
                    let lightPos = polygon.View.Inverse.GetModelOrigin()    // Position of the "light" camera

                    // Triangulate Polygon     
//                    let gpcPoly = GPC.GpcPolygon(polygon.Polygon.Points.ToArray())                   
//                    // Create lightcap Vertex Array           
//                    let lightcapVertices = 
//                        gpcPoly.ComputeTriangulation() 
//                            |> Array.map ( fun t -> [| t.P2; t.P1; t.P0 |] ) 
//                            |> Array.concat 
//                            |> Array.toList   
                    
                    let lightcapVertices    = polygon.TriangleList;                       
                    let lightcapPositions   = lightcapVertices.ToArray()   |> Array.map (fun n -> V3f(2.0 * n.X - 1.0, 1.0 - 2.0 * n.Y, 0.1))          

                    // Create Geometry for lightcap 
                    let lightcapGeometry = IndexedGeometry(       
                                                Mode = IndexedGeometryMode.TriangleList,
                                                IndexedAttributes = SymDict.ofList [ DefaultSemantic.Positions, lightcapPositions :> Array]) 
      
                     // Get the correct stencil modes and effects
                    let (stencilModeVolume, stencilModeNormalize, name, effect)= 
                        match selection with
                        |Selection.Single   -> (StencilModes.Additive, StencilModes.NormalizeAfterOR, "No Normalizaion" ,volumeAdditiveEffect)
                        |Selection.Or       -> (StencilModes.Additive, StencilModes.NormalizeAfterOR, "OR Normalizaion" ,volumeAdditiveEffect)
                        |Selection.And      -> (StencilModes.Additive, StencilModes.NormalizeAfterAND,"AND Normalizaion" ,volumeAdditiveEffect)
                        |Selection.Xor      -> (StencilModes.Additive, StencilModes.NormalizeAfterXOR,"XOR Normalizaion" ,volumeAdditiveEffect)
                        |Selection.Subtract -> (StencilModes.Subtractive, StencilModes.NormalizeAfterSUBTRACT,"SUBTRACT Normalizaion",  volumeSubtractiveEffect)
                
                    let buffers = if (renderVolumes) then Set.ofList[DefaultSemantic.Colors; DefaultSemantic.Stencil]  else (Set.singleton DefaultSemantic.Stencil)
           
                    // Create Scenegraph for light/darkcap
                    let lightcapSG = 
                        lightcapGeometry |> Sg.ofIndexedGeometry
                                         |> Sg.trafo        worldTrafo 
                                         |> Sg.viewTrafo    view             
                                         |> Sg.projTrafo    proj             
                                         |> Sg.surface      (effect                 |> Mod.constant) 
                                         |> Sg.blendMode    (BlendMode.Blend        |> Mod.constant)     
                                         |> Sg.stencilMode  (stencilModeVolume      |> Mod.constant)
                                         |> Sg.uniform      "selectionDistance" selectionDistance 
                                         |> Sg.uniform      "lightPos" (lightPos    |> Mod.constant)                                   
                                         |> Sg.pass         renderPassVolume    
                                         |> Sg.writeBuffers (Some buffers)


                    // Add Normalization Pass only if not SINGLE Selection                                   
                    let sceneGraphVolumeNormalized = 
                
                        match selection with
                        |Selection.Single -> lightcapSG                                    
                        | _               -> fullscreenQuad
                                                    |> Sg.ofIndexedGeometry
                                                    |> Sg.surface        (normalizeStencilEffect        |> Mod.constant)
                                                    |> Sg.depthTest      (Rendering.DepthTestMode.None  |> Mod.constant)                     
                                                    |> Sg.stencilMode    (stencilModeNormalize          |> Mod.constant)        
                                                    |> Sg.pass           (renderPassNormalize                          )             
                                                    |> Sg.writeBuffers   (Some (Set.singleton DefaultSemantic.Stencil) ) 
                                                    |> Sg.andAlso lightcapSG
                                    
                                                                      
                    // Return combined scenegraph             
                    let sceneGraph = Sg.group'[sceneGraphVolumeNormalized; sceneGraph]                                            
                    (sceneGraph, remainingPasses)
                | _::_                                      -> failwith "To many renderpasses used! Stop selecting you moron"

     
        // ----------------------------------------------------------------------------------------------- //
        // Inverts the current selection by flipping the stencil buffer
        // ----------------------------------------------------------------------------------------------- //
        let invertSelection(sceneGraph: ISg)(renderPassList): (ISg * list<RenderPass>) = 

            match renderPassList with
            | []                                        -> failwith "To many renderpasses used! Stop selecting you moron" // TODO RESIZE
            | renderPass1::renderPass2::remainingPasses -> 
            
                
                // Normalize Pass 1: Increment all values in the stencilbuffer
                let normalizePass1SG = 
                        fullscreenQuad
                            |> Sg.ofIndexedGeometry
                            |> Sg.surface        (normalizeStencilEffect                 |> Mod.constant)                      
                            |> Sg.stencilMode    (StencilModes.NormalizeAfterINVERTPass1 |> Mod.constant)        
                            |> Sg.pass           renderPass1     
                        
                // Normalize Pass2: Set all values greater 1 to 0               
                let normalizePass2SG = 
                        fullscreenQuad
                            |> Sg.ofIndexedGeometry
                            |> Sg.surface        (normalizeStencilEffect         |> Mod.constant)
                            |> Sg.depthTest      (Rendering.DepthTestMode.None   |> Mod.constant)                     
                            |> Sg.stencilMode    (StencilModes.NormalizeAfterXOR |> Mod.constant)        
                            |> Sg.pass           renderPass2    
                           
                let normalizeStencilSG = Sg.group'[normalizePass1SG; normalizePass2SG]

                let normalizeStencilSG = Sg.WriteBuffersApplicator(Some (Set.singleton DefaultSemantic.Stencil), (normalizeStencilSG |> Mod.constant)) :> ISg

                // Return combined scenegraph     
                let sceneGraph = Sg.group'[normalizeStencilSG;  normalizeStencilSG; sceneGraph]                                            
                (sceneGraph, remainingPasses)

            | _::_                                      -> failwith "To many renderpasses used! Stop selecting you moron"


        // ----------------------------------------------------------------------------------------------- //
        // Recoursive function for selection
        // ----------------------------------------------------------------------------------------------- //
        let rec addSelectionToSceneGraph (selection : Lasso.Selection)(sceneGraph : ISg )(renderVolumes: bool)(renderPassList) : (ISg * list<RenderPass>)=


            
                match selection with
                |Lasso.Selection.Single polygon ->  addSelectionPolygon polygon sceneGraph renderVolumes renderPassList Selection.Single                                                                                    

                // OR Selection => Recoursive add Selection
                |Lasso.Or (selection, polygon)  -> 
                                                    // Recoursive selection
                                                    //for i in 0..10 do
                                                    let (sg, remainingRenderPasses) = addSelectionToSceneGraph selection sceneGraph renderVolumes renderPassList                                                
                                                    // Add current polygon to scenegraph
                                                    addSelectionPolygon polygon sg renderVolumes remainingRenderPasses Selection.Or                                           


                // And Selection => Recoursive add Selection
                |Lasso.And (selection, polygon) -> 
                                                    // Recoursive selection
                                                    let (sg, remainingRenderPasses) = addSelectionToSceneGraph selection sceneGraph renderVolumes renderPassList                                                
                                                    // Add current polygon to scenegraph
                                                    addSelectionPolygon polygon sg renderVolumes remainingRenderPasses Selection.And   

                // XOR Selection => Recoursive add Selection
                |Lasso.Xor (selection, polygon) -> 
                                                    // Recoursive selection
                                                    let (sg, remainingRenderPasses) = addSelectionToSceneGraph selection sceneGraph renderVolumes renderPassList                                                
                                                    // Add current polygon to scenegraph
                                                    addSelectionPolygon polygon sg renderVolumes remainingRenderPasses Selection.Xor   

                // SUBTRACT Selection => Recoursive add Selection
                |Lasso.Subtract (selection, polygon) -> 
                                                    // Recoursive selection
                                                    let (sg, remainingRenderPasses) = addSelectionToSceneGraph selection sceneGraph renderVolumes renderPassList                                                
                                                    // Add current polygon to scenegraph
                                                    addSelectionPolygon polygon sg renderVolumes remainingRenderPasses Selection.Subtract   

                // INVERT Selection => Recoursive add Selection
                |Lasso.Invert (selection)            -> 
                                                        // Add selection to scenegraph
                                                        let (sg, remainingRenderPasses) = addSelectionToSceneGraph selection sceneGraph renderVolumes renderPassList                                                                                                         
                                                        // Add current polygon to scenegraph
                                                        invertSelection sg remainingRenderPasses                                            

                |Lasso.NoSelection                   -> (sceneGraph, renderPassList)          
            
                       
        // Attach Screen Space Volume Blending Effect        
        let sceneGraph = 
            fullscreenQuad
                |> Sg.ofIndexedGeometry
                |> Sg.surface        (hightlightEffect                      |> Mod.constant)
                |> Sg.depthTest      (Rendering.DepthTestMode.None          |> Mod.constant)                     
                |> Sg.blendMode      (BlendMode.Blend                       |> Mod.constant) 
                |> Sg.stencilMode    (StencilModes.stencilModeHighLightOnes |> Mod.constant)        
                |> Sg.pass           highlightSelectionPass            
                |> Sg.andAlso        sceneGraph  
        

       

        // ----------------------------------------------------------------------------------------------- //
        // Adaptive Function to listen for change in Selection
        // ----------------------------------------------------------------------------------------------- // 
        let sceneGraph = 
            adaptive {
                let! lasso = lasso.Selection.selection
                let! renderVolumes = showVolumes
                
                let (sceneGraphWithVolumes, _) = addSelectionToSceneGraph lasso sceneGraph renderVolumes renderPassList 
                
                return sceneGraphWithVolumes
            }
        
        // Return RenderPass after selection
        (sceneGraph, highlightSelectionPass)
