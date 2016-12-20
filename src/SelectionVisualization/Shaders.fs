namespace SelectionVisualization

open Aardvark.Base
open Aardvark.Base.Incremental
open FShade
open Aardvark.Base.Rendering

module Shaders =
       

    type Vertex = {
        [<Position>]        pos     : V4d
        [<SemanticAttribute("LocalPos")>] lp: V4d
        [<WorldPosition>]   wp      : V4d
        [<Color>]           c       : V4d

    }

    let trafo (v : Vertex) =
        vertex {
            let worldPos = uniform.ModelTrafo * v.pos
            return {
                lp = v.pos
                wp = worldPos
                pos = uniform.ViewProjTrafo * worldPos
                c = v.c
            }
        }


    // ----------------------------------------------------------------------------------------------- //
    // Geometry Shader to extrude the silhouette of the shadow volume in local space
    // ----------------------------------------------------------------------------------------------- //
    let ExtrudeSilhouetteLocalSpace (line : Line<Vertex>) =
        triangle {
                   
            // Model Space Position of the line
            let p0 = line.P0.lp.XYZ
            let p1 = line.P1.lp.XYZ           
            let p2 = p0 + V3d(0.0, 0.0, 1.0)
            let p3 = p1 + V3d(0.0, 0.0, 1.0)

            // Create triangle strip for extruded edge
            yield { line.P0 with pos = uniform.ModelViewProjTrafo * V4d(p0, 1.0) }
            yield { line.P1 with pos = uniform.ModelViewProjTrafo * V4d(p1, 1.0) }
            yield { line.P0 with pos = uniform.ModelViewProjTrafo * V4d(p2, 1.0) }
            yield { line.P1 with pos = uniform.ModelViewProjTrafo * V4d(p3, 1.0) }

        }

    
    // ----------------------------------------------------------------------------------------------- //
    // Geometry Shader to extrude the silhouette of the shadow volume in world space
    // ----------------------------------------------------------------------------------------------- //
    let ExtrudeSilhouette(line : Line<Vertex>) =
        triangle {
            
            let lightPos : V3d = uniform?lightPos

            // Positions in world space
            let p0 = line.P0.wp / line.P0.wp.W
            let p1 = line.P1.wp / line.P1.wp.W
           
            // Directions from light source to vertices of triangle
            let dir0 = (p0.XYZ - lightPos).Normalized
            let dir1 = (p1.XYZ - lightPos).Normalized

            let p00 = V4d(p0.XYZ + 5.0 * dir0, 1.0);
            let p11 = V4d(p1.XYZ + 5.0 * dir1, 1.0);

            yield { line.P0 with pos = uniform.ViewProjTrafo * p1 }
            yield { line.P1 with pos = uniform.ViewProjTrafo * p0 }
            yield { line.P0 with pos = uniform.ViewProjTrafo * p00}                  
            endPrimitive()
            yield { line.P0 with pos = uniform.ViewProjTrafo * p1 }
            yield { line.P1 with pos = uniform.ViewProjTrafo * p00}
            yield { line.P0 with pos = uniform.ViewProjTrafo * p11}  
        }


    // ----------------------------------------------------------------------------------------------- //
    // Geometry Shader extrude a shadow volume from a triangulated light cap polygon
    // ----------------------------------------------------------------------------------------------- //
    let ExtrudeCaps (t: Triangle<Vertex>) = 
        triangle {

            let lightPos : V3d = uniform?lightPos
           // let selectionDistance: float = uniform?selectionDistance
            let selectionDistance : float = uniform?selectionDistance
            // Read Positions in World Space = Points at the light cap
            let p0 = t.P0.wp / t.P0.wp.W
            let p1 = t.P1.wp / t.P1.wp.W
            let p2 = t.P2.wp / t.P2.wp.W
            
            // Directions from Lightsource to vertices of triangle
            let dir0 = (p0.XYZ - lightPos).Normalized
            let dir1 = (p1.XYZ - lightPos).Normalized
            let dir2 = (p2.XYZ - lightPos).Normalized

            // Points at the dark cap // CHANGE TO INFINTIY           
            let p00 = V4d(p0.XYZ + selectionDistance * dir0, 1.0)
            let p11 = V4d(p1.XYZ + selectionDistance * dir1, 1.0)
            let p22 = V4d(p2.XYZ + selectionDistance * dir2, 1.0)


            // Emit Light cap
            yield {t.P0 with pos = uniform.ViewProjTrafo * p0}
            yield {t.P1 with pos = uniform.ViewProjTrafo * p1}
            yield {t.P2 with pos = uniform.ViewProjTrafo * p2}          
            endPrimitive()

            // Emit Dark cap
            yield {t.P2 with pos = uniform.ViewProjTrafo * p22}     
            yield {t.P1 with pos = uniform.ViewProjTrafo * p11}       
            yield {t.P0 with pos = uniform.ViewProjTrafo * p00}                                             
            endPrimitive()

            // Emit quad for each Line

            // LINE 01            
            yield { t.P0 with pos = uniform.ViewProjTrafo * p1 }
            yield { t.P1 with pos = uniform.ViewProjTrafo * p0 }
            yield { t.P0 with pos = uniform.ViewProjTrafo * p00}                  
            endPrimitive()
            yield { t.P0 with pos = uniform.ViewProjTrafo * p1 }
            yield { t.P1 with pos = uniform.ViewProjTrafo * p00}
            yield { t.P0 with pos = uniform.ViewProjTrafo * p11}               
            endPrimitive()
            
            // LINE 12
            yield { t.P0 with pos = uniform.ViewProjTrafo * p2 }
            yield { t.P1 with pos = uniform.ViewProjTrafo * p1 }
            yield { t.P0 with pos = uniform.ViewProjTrafo * p11}                  
            endPrimitive()
            yield { t.P0 with pos = uniform.ViewProjTrafo * p2 }
            yield { t.P1 with pos = uniform.ViewProjTrafo * p11}
            yield { t.P0 with pos = uniform.ViewProjTrafo * p22}               
            endPrimitive()

            // LINE 20
            yield { t.P0 with pos = uniform.ViewProjTrafo * p0 }
            yield { t.P1 with pos = uniform.ViewProjTrafo * p2 }
            yield { t.P0 with pos = uniform.ViewProjTrafo * p22}                  
            endPrimitive()
            yield { t.P0 with pos = uniform.ViewProjTrafo * p0 }
            yield { t.P1 with pos = uniform.ViewProjTrafo * p22}
            yield { t.P0 with pos = uniform.ViewProjTrafo * p00 }               

        }