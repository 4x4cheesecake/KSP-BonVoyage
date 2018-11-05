﻿using System.Collections.Generic;
using UnityEngine;

namespace BonVoyage
{
    /// <summary>
    /// Move vessel up/down to fix wrong heigh report for lon/lat from KSP
    /// Borrowed from World Stabilizer and changed
    /// </summary>
    internal static class StabilizeVessel
    {
        // How many ticks do we hold the vessel
        private static int stabilizationTicks = 100;

        // If downmovement is below this value, leave the vessel as is
        private static float minDownMovement = 0.05f;
        // Minimum upmovement in case we're beneath the ground
        private static float upMovementStep = 0.2f;
        // Max upmovement in case upward movement is required; should cancel moving the craft to space in case we messed the things up
        private static float maxUpMovement = 2.0f;
        // Last resort drop altitude
        // If the mod can't reliably determine the height above obstacles, like when vessel lies on different colliders, it still will be lowered, but to this altitude
        private static float lastResortAltitude = 2.0f;

        private const int rayCastMask = (1 << 28) | (1 << 15);
        private const int rayCastExtendedMask = rayCastMask | 1;

        private static Vessel vesselToStabilize = null;
        private static int vesselTimer = stabilizationTicks;
        private static bool moveVesselUp = false;
        private static VesselBounds bounds;
        private static double initialAltitude = 0;


        /// <summary>
        /// This vessel will be stabilized
        /// </summary>
        /// <param name="v"></param>
        internal static void AddVesselToStabilize(Vessel v)
        {
            vesselToStabilize = v;
            if (vesselToStabilize == null)
                return;

            vesselTimer = stabilizationTicks;
            moveVesselUp = true;
            bounds = new VesselBounds(v);
            initialAltitude = v.altitude;
        }


        /// <summary>
        /// Stabilize vessel if there is some
        /// </summary>
        internal static void Stabilize()
        {
            if (vesselToStabilize == null)
                return;

            if (vesselTimer > 0)
            {
                Stabilize(vesselToStabilize);

                vesselTimer--;
                if (vesselTimer == 0)
                {
                    BVController controller = BonVoyage.Instance.GetControllerOfVessel(vesselToStabilize);
                    if (controller != null)
                        controller.Arrived = false;
                    vesselToStabilize = null;
                }
            }
        }


        /// <summary>
        /// Stabilization function
        /// </summary>
        private static void Stabilize(Vessel v)
        {
            // First, we move burrowed vessel up and then down if it's too high
            if (moveVesselUp)
            {
                MoveUp(v);
                moveVesselUp = false;
            }
            else
                MoveDown(v);

            v.ResetGroundContact();
            v.IgnoreGForces(20);
            v.SetWorldVelocity(Vector3.zero);
            v.angularMomentum = Vector3.zero;
            v.angularVelocity = Vector3.zero;
            VesselSleep(v);
        }


        /// <summary>
        /// Sleep vessel
        /// </summary>
        /// <param name="v"></param>
        private static void VesselSleep(Vessel v)
        {
            foreach (Part p in v.parts)
            {
                if (p.Rigidbody != null)
                    p.Rigidbody.Sleep();
            }
        }


        /// <summary>
        /// Move vessel down
        /// </summary>
        /// <param name="v"></param>
        private static void MoveDown(Vessel v)
        {
            bounds.findBoundPoints();

            RayCastResult alt = GetRaycastAltitude(v, bounds.bottomPoint, rayCastMask);
            RayCastResult alt2 = GetRaycastAltitude(v, bounds.topPoint, rayCastMask);

            Vector3 referencePoint = bounds.bottomPoint;
            if (alt.collider != alt2.collider)
            {
                minDownMovement = lastResortAltitude;
                if (alt2.altitude < alt.altitude)
                    referencePoint = bounds.topPoint;
            }

            // Re-cast raycast including parts into the mask
            alt = GetRaycastAltitude(v, referencePoint, rayCastExtendedMask);
            float downMovement = alt.altitude;

            Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;

            if (downMovement < minDownMovement)
                return;

            downMovement -= minDownMovement;

            v.Translate(-downMovement * (Vector3d)up);
        }


        /// <summary>
        /// Move vessel up
        /// </summary>
        /// <param name="v"></param>
        private static void MoveUp(Vessel v)
        {
            v.ResetCollisionIgnores();

            // TODO: Try DisableSuspension() on wheels

            float upMovement = 0.0f;

            float vesselHeight = bounds.topLength + bounds.bottomLength;
            Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;

            while (upMovement < maxUpMovement)
            {
                RayCastResult alt = GetRaycastAltitude(v, bounds.bottomPoint + up * vesselHeight, rayCastMask); // mask: ground only
                
                if (alt.altitude - vesselHeight < minDownMovement)
                {
                    v.Translate(up * upMovementStep);
                    upMovement += upMovementStep;
                }
                else
                    break;
            }
        }


        internal class Pair<T, U>
        {
            internal Pair()
            {
            }

            internal Pair(T first, U second)
            {
                this.First = first;
                this.Second = second;
            }

            internal T First { get; set; }
            internal U Second { get; set; }
        }


        /// <summary>
        /// Bounds of a vessel
        /// </summary>
        internal struct VesselBounds
        {

            internal Vessel vessel;
            internal float bottomLength;
            internal float topLength;

            internal Vector3 localBottomPoint;
            internal Vector3 bottomPoint
            {
                get
                {
                    return vessel.transform.TransformPoint(localBottomPoint);
                }
            }

            internal Vector3 localTopPoint;
            internal Vector3 topPoint
            {
                get
                {
                    return vessel.transform.TransformPoint(localTopPoint);
                }
            }

            internal Vector3 up;
            internal float maxSuspensionTravel;
            internal Part bottomPart;


            internal VesselBounds(Vessel v)
            {
                vessel = v;
                bottomLength = 0;
                topLength = 0;
                localBottomPoint = Vector3.zero;
                localTopPoint = Vector3.zero;
                up = Vector3.zero;
                maxSuspensionTravel = 0f;
                bottomPart = v.rootPart;
                findBoundPoints();
            }


            internal void findBoundPoints()
            {
                Vector3 lowestPoint = Vector3.zero;
                Vector3 highestPoint = Vector3.zero;

                Part downwardFurthestPart = vessel.rootPart;
                Part upwardFurthestPart = vessel.rootPart;
                up = (vessel.CoM - vessel.mainBody.transform.position).normalized;
                Vector3 downPoint = vessel.CoM - (2000 * up);
                Vector3 upPoint = vessel.CoM + (2000 * up);
                Vector3 closestVert = vessel.CoM;
                Vector3 farthestVert = vessel.CoM;
                float closestSqrDist = Mathf.Infinity;
                float farthestSqrDist = Mathf.Infinity;

                foreach (Part p in vessel.parts)
                {

                    if (p.Modules.Contains("KASModuleHarpoon"))
                        continue;

                    HashSet<Pair<Transform, Mesh>> meshes = new HashSet<Pair<Transform, Mesh>>();
                    foreach (MeshFilter filter in p.GetComponentsInChildren<MeshFilter>())
                    {

                        Collider[] cdr = filter.GetComponentsInChildren<Collider>();
                        if (cdr.Length > 0 || p.Modules.Contains("ModuleWheelSuspension"))
                        {
                            // for whatever reason suspension needs an additional treatment
                            // TODO: Maybe address it by searching for wheel collider
                            meshes.Add(new Pair<Transform, Mesh>(filter.transform, filter.mesh));
                        }
                    }

                    foreach (MeshCollider mcdr in p.GetComponentsInChildren<MeshCollider>())
                        meshes.Add(new Pair<Transform, Mesh>(mcdr.transform, mcdr.sharedMesh));

                    foreach (Pair<Transform, Mesh> meshpair in meshes)
                    {
                        Mesh mesh = meshpair.Second;
                        Transform tr = meshpair.First;
                        foreach (Vector3 vert in mesh.vertices)
                        {
                            //bottom check
                            Vector3 worldVertPoint = tr.TransformPoint(vert);
                            float bSqrDist = (downPoint - worldVertPoint).sqrMagnitude;
                            if (bSqrDist < closestSqrDist)
                            {
                                closestSqrDist = bSqrDist;
                                closestVert = worldVertPoint;
                                downwardFurthestPart = p;

                                // TODO: Not used at the moment, but we might infer amount of 
                                // TODO: upward movement from this 
                                // If this is a landing gear, account for suspension compression
                                /*if (p.Modules.Contains ("ModuleWheelSuspension")) {
									ModuleWheelSuspension suspension = p.GetComponent<ModuleWheelSuspension> ();
									if (maxSuspensionTravel < suspension.suspensionDistance)
										maxSuspensionTravel = suspension.suspensionDistance;
									printDebug ("Suspension: dist=" + suspension.suspensionDistance + "; offset="
										+ suspension.suspensionOffset + "; pos=(" + suspension.suspensionPos.x + "; "
										+ suspension.suspensionPos.y + "; " + suspension.suspensionPos.z + ")");
								}*/
                            }
                            bSqrDist = (upPoint - worldVertPoint).sqrMagnitude;
                            if (bSqrDist < farthestSqrDist)
                            {
                                farthestSqrDist = bSqrDist;
                                farthestVert = worldVertPoint;
                                upwardFurthestPart = p;
                            }
                        }
                    }
                }

                bottomLength = Vector3.Project(closestVert - vessel.CoM, up).magnitude;
                localBottomPoint = vessel.transform.InverseTransformPoint(closestVert);
                topLength = Vector3.Project(farthestVert - vessel.CoM, up).magnitude;
                localTopPoint = vessel.transform.InverseTransformPoint(farthestVert);
                bottomPart = downwardFurthestPart;
            }
        }


        internal class RayCastResult
        {
            internal Collider collider;
            internal float altitude;

            internal RayCastResult()
            {
                collider = null;
                altitude = 0.0f;
            }
        }


        /// <summary>
        /// Get altitude of a RayCast
        /// </summary>
        /// <param name="v"></param>
        /// <param name="originPoint"></param>
        /// <param name="layerMask"></param>
        /// <returns></returns>
        internal static RayCastResult GetRaycastAltitude(Vessel v, Vector3 originPoint, int layerMask)
        {
            RaycastHit hit;
            Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
            RayCastResult result = new RayCastResult();
            if (Physics.Raycast(originPoint, -up, out hit, v.vesselRanges.landed.unload, layerMask))
            {
                result.altitude = Vector3.Project(hit.point - originPoint, up).magnitude;
                result.collider = hit.collider;
            }
            return result;
        }

    }

}
