namespace VolumeSelection

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Rendering.NanoVg
open Aardvark.SceneGraph
open Aardvark.Base.Rendering

open System
open System.Linq

open Aardvark.VRVis

open ShadowVolumeShader
open Lasso

module VolumeSelection =

    type Selection =
    | Single
    | And
    | Or
    | Xor
    | Subtract
    

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
    let Init (sceneGraph: ISg)(view: IMod<Trafo3d>)(proj: IMod<Trafo3d>)(lasso : MouseKeyboard.VgmLasso)(selectionColor: IMod<C4f>)(selectionDistance: IMod<double>)(volumeColor: IMod<C4f>)(showVolumes : IMod<bool>)(geometryPass: RenderPass)(runtime : IRuntime)(framebufferSignature: IFramebufferSignature) : (IMod<ISg> * IMod<RenderPass>) = 
      
        let mutable renderVolumes = false
        
        let invVolumeColor = volumeColor |> Mod.map (fun c-> (C4f(1.0f - c.R, 1.0f - c.G, 1.0f - c.B, c.A)))
 
        // Initialize Shading Effects
        let volumeAdditiveEffect    = runtime.PrepareEffect(framebufferSignature, [ShadowVolumeShader.trafo |> toEffect;  ShadowVolumeShader.ExtrudeCaps |> toEffect; DefaultSurfaces.uniformColor volumeColor  |> toEffect]) :> ISurface
        let volumeSubtractiveEffect = runtime.PrepareEffect(framebufferSignature, [ShadowVolumeShader.trafo |> toEffect;  ShadowVolumeShader.ExtrudeCaps |> toEffect; DefaultSurfaces.uniformColor invVolumeColor  |> toEffect]) :> ISurface
                          
        let normalizeStencilEffect = runtime.PrepareEffect(framebufferSignature, [DefaultSurfaces.constantColor C4f.Black |> toEffect]) :> ISurface         
        let hightlightEffect       = runtime.PrepareEffect(framebufferSignature, [DefaultSurfaces.uniformColor selectionColor |> toEffect]) :> ISurface         
        
        // Full Screen Quad Geometry
        let fullscreenQuad = IndexedGeometry (
                                Mode = IndexedGeometryMode.TriangleList, IndexArray = [| 0; 1; 2; 0; 2; 3 |],
                                       IndexedAttributes = SymDict.ofList [DefaultSemantic.Positions, [| V3f(-1.0, -1.0, 1.0); V3f(1.0, -1.0, 1.0); V3f(1.0, 1.0, 1.0); V3f(-1.0, 1.0, 1.0) |] :> Array])                         
        

        // ----------------------------------------------------------------------------------------------- //
        // Adds a Selection polygon
        // ----------------------------------------------------------------------------------------------- //
        let addSelectionPolygon(polygon : Lasso.NearPlanePolygon)(sceneGraph : ISg)(renderPass: RenderPass)(selection: Selection): (ISg * RenderPass) =
           
            let worldTrafo = (polygon.View * polygon.Proj).Inverse |> Mod.constant  // Transformation into world space 
            let lightPos = polygon.View.Inverse.GetModelOrigin()    // Position of the "light" camera
            
            let renderPassVolume = renderPass;
            let renderPassNormalize = RenderPass.after "NormalizePass" RenderPassOrder.Arbitrary renderPassVolume

            // Triangulate Polygon     
            let gpcPoly = Aardvark.VRVis.GpcPolygon(polygon.Polygon.Points.ToArray())                   
            // Create lightcap Vertex Array           
            let lightcapVertices = 
                gpcPoly.ComputeTriangulation() 
                    |> Array.map ( fun t -> [| t.P2; t.P1; t.P0 |] ) 
                    |> Array.concat 
                    |> Array.toList   
                                   
            let lightcapPositions   = lightcapVertices.ToArray()   |> Array.map (fun n -> V3d(2.0 * n.X - 1.0, 1.0 - 2.0 * n.Y, 0.0))          

            // Create Geometry for lightcap 
            let lightcapGeometry = IndexedGeometry(       
                                        Mode = IndexedGeometryMode.TriangleList,
                                        IndexedAttributes = SymDict.ofList [ DefaultSemantic.Positions, lightcapPositions :> Array]) 
      
             // Get the correct stencil modes and effects
            let (stencilModeVolume, stencilModeNormalize, effect)= 
                match selection with
                |Selection.Single   -> (StencilModes.Additive, StencilModes.NormalizeAfterOR,  volumeAdditiveEffect)
                |Selection.Or       -> (StencilModes.Additive, StencilModes.NormalizeAfterOR,  volumeAdditiveEffect)
                |Selection.And      -> (StencilModes.Additive, StencilModes.NormalizeAfterAND, volumeAdditiveEffect)
                |Selection.Xor      -> (StencilModes.Additive, StencilModes.NormalizeAfterXOR, volumeAdditiveEffect)
                |Selection.Subtract -> (StencilModes.Subtractive, StencilModes.NormalizeAfterSUBTRACT, volumeSubtractiveEffect)
                

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
            

            let buffers = if (renderVolumes) then Set.ofList[DefaultSemantic.Colors]  else Set.empty
            let volumeSG = Sg.WriteBuffersApplicator(Some buffers, (lightcapSG |> Mod.constant))   :> ISg                                                    

            let normalizeStencilSG = 
                    fullscreenQuad
                        |> Sg.ofIndexedGeometry
                        |> Sg.surface        (normalizeStencilEffect        |> Mod.constant)
                        |> Sg.depthTest      (Rendering.DepthTestMode.None  |> Mod.constant)                     
                        |> Sg.stencilMode    (stencilModeNormalize          |> Mod.constant)        
                        |> Sg.pass           renderPassNormalize               
            
            let normalizeStencilSG = Sg.WriteBuffersApplicator(Some Set.empty, (normalizeStencilSG |> Mod.constant)) :>ISg       
                        
            // Return combined scenegraph             
            let sceneGraph = Sg.group'[volumeSG;  normalizeStencilSG; sceneGraph]                                            
            (sceneGraph, renderPassNormalize)
             
     
        // ----------------------------------------------------------------------------------------------- //
        // Inverts the current selection by flipping the stencil buffer
        // ----------------------------------------------------------------------------------------------- //
        let invertSelection(sceneGraph: ISg)(renderPass: RenderPass): (ISg * RenderPass) = 

            let renderPass1 = Rendering.RenderPass.after "InvertPass1" RenderPassOrder.Arbitrary renderPass
            let renderPass2 = Rendering.RenderPass.after "InvertPass2" RenderPassOrder.Arbitrary renderPass1
            
            // Normalize Pass 1: Increment all values in the stencilbuffer
            let normalizePass1SG = 
                    fullscreenQuad
                        |> Sg.ofIndexedGeometry
                        |> Sg.surface        (normalizeStencilEffect        |> Mod.constant)
                        //|> Sg.depthTest      (Rendering.DepthTestMode.  |> Mod.constant)                         
                        |> Sg.stencilMode    (StencilModes.NormalizeAfterINVERTPass1 |> Mod.constant)        
                        |> Sg.pass           renderPass1     
           
            // Normalize Pass2: Set all values greater 1 to 0               
            let normalizePass2SG = 
                    fullscreenQuad
                        |> Sg.ofIndexedGeometry
                        |> Sg.surface        (normalizeStencilEffect        |> Mod.constant)
                        |> Sg.depthTest      (Rendering.DepthTestMode.None  |> Mod.constant)                     
                        |> Sg.stencilMode    (StencilModes.NormalizeAfterINVERTPass2 |> Mod.constant)        
                        |> Sg.pass           renderPass2    
                           
            let normalizeStencilSG = Sg.group'[normalizePass1SG; normalizePass2SG]
            let normalizeStencilSG = Sg.WriteBuffersApplicator(Some Set.empty, (normalizeStencilSG |> Mod.constant)) :> ISg

            // Return combined scenegraph     
            let sceneGraph = Sg.group'[normalizeStencilSG;  normalizeStencilSG; sceneGraph]                                            
            (sceneGraph, renderPass2)


        // ----------------------------------------------------------------------------------------------- //
        // Recoursive function for selection
        // ----------------------------------------------------------------------------------------------- //
        let rec addSelectionToSceneGraph (selection : Lasso.Selection)(sceneGraph : ISg )(renderPassBefore: RenderPass) : (ISg * RenderPass)=
           
            
            match selection with
            |Lasso.Selection.Single polygon -> let renderPassCurrent = RenderPass.after "SINGLE_Selection" RenderPassOrder.Arbitrary renderPassBefore
                                               let (sg, renderPassAfter) = addSelectionPolygon polygon sceneGraph renderPassCurrent Selection.Or                                  
                                               (sg, renderPassAfter)

            // OR Selection => Recoursive add Selection
            |Lasso.Or (selection, polygon)  -> let renderPassCurrent = RenderPass.after "OR_Selection" RenderPassOrder.Arbitrary renderPassBefore
                                               // Add selection to scenegraph
                                               let (sg, renderPassAfter) = addSelectionToSceneGraph selection sceneGraph renderPassCurrent 
                                               // Add current polygon to scenegraph
                                               let (sg, renderPassAfterAfter) = addSelectionPolygon polygon sg renderPassAfter Selection.Or                                           
                                               (sg, renderPassAfterAfter)

            // And Selection => Recoursive add Selection
            |Lasso.And (selection, polygon) -> let renderPassCurrent = RenderPass.after "AND_Selection" RenderPassOrder.Arbitrary renderPassBefore
                                               // Add selection to scenegraph
                                               let (sg, renderPassAfter) = addSelectionToSceneGraph selection sceneGraph renderPassCurrent 
                                               // Add current polygon to scenegraph
                                               let (sg, renderPassAfterAfter) = addSelectionPolygon polygon sg renderPassAfter Selection.And                                           
                                               (sg, renderPassAfterAfter)

            // XOR Selection => Recoursive add Selection
            |Lasso.Xor (selection, polygon) -> let renderPassCurrent = RenderPass.after "XOR_Selection" RenderPassOrder.Arbitrary renderPassBefore
                                               // Add selection to scenegraph
                                               let (sg, renderPassAfter) = addSelectionToSceneGraph selection sceneGraph renderPassCurrent
                                               // Add current polygon to scenegraph
                                               let (sg, renderPassAfterAfter) = addSelectionPolygon polygon sg renderPassAfter Selection.Xor                                           
                                               (sg, renderPassAfterAfter)

            // SUBTRACT Selection => Recoursive add Selection
            |Lasso.Subtract (selection, polygon) -> let renderPassCurrent = RenderPass.after "SUBTRACT_Selection" RenderPassOrder.Arbitrary renderPassBefore
                                                    // Add selection to scenegraph
                                                    let (sg, renderPassAfter) = addSelectionToSceneGraph selection sceneGraph renderPassCurrent
                                                    // Add current polygon to scenegraph
                                                    let (sg, renderPassAfterAfter) = addSelectionPolygon polygon sg renderPassAfter Selection.Subtract                                           
                                                    (sg, renderPassAfterAfter)

            // INVERT Selection => Recoursive add Selection
            |Lasso.Invert (selection)            -> let renderPassCurrent = RenderPass.after "INVERT_Selection" RenderPassOrder.Arbitrary renderPassBefore
                                                    // Add selection to scenegraph
                                                    let (sg, renderPassAfter) = addSelectionToSceneGraph selection sceneGraph renderPassCurrent
                                                    // Add current polygon to scenegraph
                                                    let (sg, renderPassAfterAfter) = invertSelection sg renderPassAfter                                            
                                                    (sg, renderPassAfterAfter)


            |Lasso.NoSelection                   -> (sceneGraph, renderPassBefore)          

        
        // ----------------------------------------------------------------------------------------------- //
        // Adaptive Function to listen for change in Selection
        // ----------------------------------------------------------------------------------------------- // 
        let sg_and_renderpass = 
            adaptive {
                let! lasso = lasso.Selection.selection
                renderVolumes <- showVolumes.GetValue()
                let (sceneGraphWithVolumes, volumePass) = addSelectionToSceneGraph lasso sceneGraph geometryPass
                let highlightSelectionPass = Rendering.RenderPass.after "Highlight_SelectionPass" Rendering.RenderPassOrder.Arbitrary volumePass              
               
                // Attach Screen Space Volume Blending Effect        
                let sceneGraph = 
                    fullscreenQuad
                        |> Sg.ofIndexedGeometry
                        |> Sg.surface        (hightlightEffect                  |> Mod.constant)
                        |> Sg.depthTest      (Rendering.DepthTestMode.None      |> Mod.constant)                     
                        |> Sg.blendMode      (BlendMode.Blend                   |> Mod.constant) 
                        |> Sg.stencilMode    (StencilModes.stencilModeHighLight |> Mod.constant)        
                        |> Sg.pass           highlightSelectionPass            
                        |> Sg.andAlso        sceneGraphWithVolumes  
                                                        
                
                return (sceneGraph, highlightSelectionPass)
            }
        
                       
        let scenegraph = (sg_and_renderpass |> Mod.map  fst)
        let pass       = (sg_and_renderpass |> Mod.map  snd)                 

        // Return RenderPass after selection
        (scenegraph, pass)
