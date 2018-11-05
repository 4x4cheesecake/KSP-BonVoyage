using KSP.Localization;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BonVoyage
{
    /// <summary>
    /// Rover controller. Child of BVController
    /// </summary>
    internal class RoverController : BVController
    {
        #region internal properties

        internal override double AverageSpeed { get { return ((angle <= 90) ? (averageSpeed * speedMultiplier) : (averageSpeedAtNight * speedMultiplier)); } }

        #endregion


        #region Private properties

        // Config values
        private double averageSpeed = 0;
        private double averageSpeedAtNight = 0;
        private bool manned = false;
        private double vesselHeightFromTerrain = 0;
        // Config values

        private double speedMultiplier;
        private double angle; // Angle between the main body and the main sun
        private double electricPower_Solar; // Electric power from solar panels
        private double electricPower_Other; // Electric power from other power sources
        private double requiredPower; // Power required by wheels
        private double maxSpeedBase; // maximum speed without modifiers
        private int wheelsPercentualModifier; // Speed modifier based on wheels
        private int crewSpeedBonus; // Speed modifier based on the available crew
        WheelTestResult wheelTestResult = new WheelTestResult();

        // Reduction of speed based on difference between required and available power in percents
        private double SpeedReduction
        {
            get
            {
                double speedReduction = 0;
                if (requiredPower > (electricPower_Solar + electricPower_Other))
                    speedReduction = (requiredPower - (electricPower_Solar + electricPower_Other)) / requiredPower * 100;
                return speedReduction;
            }
        }

        #endregion


        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="v"></param>
        /// <param name="module"></param>
        internal RoverController(Vessel v, ConfigNode module) : base (v, module)
        {
            // Load values from config
            averageSpeed = double.Parse(BVModule.GetValue("averageSpeed") != null ? BVModule.GetValue("averageSpeed") : "0");
            averageSpeedAtNight = double.Parse(BVModule.GetValue("averageSpeedAtNight") != null ? BVModule.GetValue("averageSpeedAtNight") : "0");
            manned = bool.Parse(BVModule.GetValue("manned") != null ? BVModule.GetValue("manned") : "false");
            vesselHeightFromTerrain = double.Parse(BVModule.GetValue("vesselHeightFromTerrain") != null ? BVModule.GetValue("vesselHeightFromTerrain") : "0");

            speedMultiplier = 1.0;
            angle = 0;
            electricPower_Solar = 0;
            electricPower_Other = 0;
            requiredPower = 0;
            maxSpeedBase = 0;
            wheelsPercentualModifier = 0;
            crewSpeedBonus = 0;
        }


        /// <summary>
        /// Get controller type
        /// </summary>
        /// <returns></returns>
        internal override int GetControllerType()
        {
            return 0;
        }


        #region Status window texts

        internal override List<DisplayedSystemCheckResult> GetDisplayedSystemCheckResults()
        {
            base.GetDisplayedSystemCheckResults();

            DisplayedSystemCheckResult result = new DisplayedSystemCheckResult
            {
                Label = Localizer.Format("#LOC_BV_Control_AverageSpeed"),
                Text = averageSpeed.ToString("F") + " m/s",
                Tooltip = 
                    averageSpeed > 0
                    ?
                    Localizer.Format("#LOC_BV_Control_SpeedBase") + ": " + maxSpeedBase.ToString("F") + " m/s\n"
                        + Localizer.Format("#LOC_BV_Control_WheelsModifier") + ": " + wheelsPercentualModifier.ToString("F") + "%\n"
                        + (manned ? Localizer.Format("#LOC_BV_Control_DriverBonus") + ": " + crewSpeedBonus.ToString() + "%\n" : Localizer.Format("#LOC_BV_Control_UnmannedPenalty") + ": 80%\n")
                        + (SpeedReduction > 0 ? Localizer.Format("#LOC_BV_Control_PowerPenalty") + ": " + (SpeedReduction > 75 ? "100" : SpeedReduction.ToString("F")) + "%\n" : "")
                        + Localizer.Format("#LOC_BV_Control_SpeedAtNight") + ": " + averageSpeedAtNight.ToString("F") + " m/s"
                    :
                    Localizer.Format("#LOC_BV_Control_WheelsNotOnline")
            };
            displayedSystemCheckResults.Add(result);

            result = new DisplayedSystemCheckResult
            {
                Label = Localizer.Format("#LOC_BV_Control_GeneratedPower"),
                Text = (electricPower_Solar + electricPower_Other).ToString("F"),
                Tooltip = Localizer.Format("#LOC_BV_Control_SolarPower") + ": " + electricPower_Solar.ToString("F") + "\n" + Localizer.Format("#LOC_BV_Control_GeneratorPower") + ": " + electricPower_Other.ToString("F")
            };
            displayedSystemCheckResults.Add(result);

            result = new DisplayedSystemCheckResult
            {
                Label = Localizer.Format("#LOC_BV_Control_RequiredPower"),
                Text = requiredPower.ToString("F") 
                    + (SpeedReduction == 0 ? "" : 
                        (((SpeedReduction > 0) && (SpeedReduction <= 75)) 
                            ? " (" + Localizer.Format("#LOC_BV_Control_PowerReduced") + " " + SpeedReduction.ToString("F") + "%)" 
                            : " (" + Localizer.Format("#LOC_BV_Control_NotEnoughPower") + ")")),
                Tooltip = ""
            };
            displayedSystemCheckResults.Add(result);

            return displayedSystemCheckResults;
        }

        #endregion


        #region Pathfinder

        /// <summary>
        /// Find a route to the target
        /// </summary>
        /// <param name="lat"></param>
        /// <param name="lon"></param>
        /// <returns></returns>
        internal override bool FindRoute(double lat, double lon)
        {
            return FindRoute(lat, lon, TileTypes.Land);
        }

        #endregion


        /// <summary>
        /// Check the systems
        /// </summary>
        internal override void SystemCheck()
        {
            base.SystemCheck();

            // Test stock wheels
            WheelTestResult testResultStockWheels = CheckStockWheels();
            // Test KSPWheels
            WheelTestResult testResultKSPkWheels = CheckKSPWheels();
            // Sum it
            wheelTestResult.powerRequired = testResultStockWheels.powerRequired + testResultKSPkWheels.powerRequired;
            wheelTestResult.maxSpeedSum = testResultStockWheels.maxSpeedSum + testResultKSPkWheels.maxSpeedSum;
            wheelTestResult.inTheAir = testResultStockWheels.inTheAir + testResultKSPkWheels.inTheAir;
            wheelTestResult.operable = testResultStockWheels.operable + testResultKSPkWheels.operable;
            wheelTestResult.damaged = testResultStockWheels.damaged + testResultKSPkWheels.damaged;
            wheelTestResult.online = testResultStockWheels.online + testResultKSPkWheels.online;
            wheelTestResult.maxWheelRadius = testResultStockWheels.maxWheelRadius + testResultKSPkWheels.maxWheelRadius;

            // Generally, moving at high speed requires less power than wheels' max consumption. Maximum required power of controller will 35% of wheels power requirement 
            requiredPower = wheelTestResult.powerRequired / 100 * 35;

            // Get power production
            electricPower_Solar = GetAvailablePower_Solar();
            electricPower_Other = GetAvailablePower_Other();

            // Manned
            manned = (vessel.GetCrewCount() > 0);

            // Pilots and Scouts (USI) increase base average speed
            crewSpeedBonus = 0;
            if (manned)
            {
                int maxPilotLevel = -1;
                int maxScoutLevel = -1;
                int maxDriverLevel = -1;

                List<ProtoCrewMember> crewList = vessel.GetVesselCrew();
                for (int i = 0; i < crewList.Count; i++)
                {
                    switch (crewList[i].trait)
                    {
                        case "Pilot":
                            if (maxPilotLevel < crewList[i].experienceLevel)
                                maxPilotLevel = crewList[i].experienceLevel;
                            break;
                        case "Scout":
                            if (maxScoutLevel < crewList[i].experienceLevel)
                                maxScoutLevel = crewList[i].experienceLevel;
                            break;
                        default:
                            if (crewList[i].HasEffect("AutopilotSkill"))
                                if (maxDriverLevel < crewList[i].experienceLevel)
                                    maxDriverLevel = crewList[i].experienceLevel;
                            break;
                    }
                }
                if (maxPilotLevel > 0)
                    crewSpeedBonus = 6 * maxPilotLevel; // up to 30% for a Pilot
                else if (maxDriverLevel > 0)
                    crewSpeedBonus = 4 * maxDriverLevel; // up to 20% for any driver (has AutopilotSkill skill)
                else if (maxScoutLevel > 0)
                    crewSpeedBonus = 2 * maxScoutLevel; // up to 10% for a Scout (Scouts disregard safety)
            }

            // Average speed will vary depending on number of wheels online and crew present from 50 to 95 percent of average wheels' max speed
            if (wheelTestResult.online != 0)
            {
                maxSpeedBase = wheelTestResult.maxSpeedSum / wheelTestResult.online;
                wheelsPercentualModifier = Math.Min(70, (40 + 5 * wheelTestResult.online));
                averageSpeed = maxSpeedBase * wheelsPercentualModifier / 100 * (1 + crewSpeedBonus / 100);
            }
            else
                averageSpeed = 0;

            // Unmanned rovers drive with 80% speed penalty
            if (!manned)
                averageSpeed = averageSpeed * 0.2;

            // Base average speed at night is the same as average speed, if there is other power source. Zero otherwise.
            if (electricPower_Other > 0.0)
                averageSpeedAtNight = averageSpeed;
            else
                averageSpeedAtNight = 0;

            // If required power is greater then total power generated, then average speed can be lowered up to 75%
            if (requiredPower > (electricPower_Solar + electricPower_Other))
            {
                double speedReduction = (requiredPower - (electricPower_Solar + electricPower_Other)) / requiredPower;
                if (speedReduction <= 0.75)
                    averageSpeed = averageSpeed * (1 - speedReduction);
            }

            // If required power is greater then generated other power generated, then average speed at night can be lowered up to 75%
            if (requiredPower > electricPower_Other)
            {
                double speedReduction = (requiredPower - electricPower_Other) / requiredPower;
                if (speedReduction <= 0.75)
                    averageSpeedAtNight = averageSpeedAtNight * (1 - speedReduction);
                else
                    averageSpeedAtNight = 0;
            }
        }


        #region Wheels

        /// <summary>
        /// Result of the test of wheels
        /// </summary>
        private struct WheelTestResult
        {
            internal double powerRequired; // Total power required
            internal double maxSpeedSum; // Sum of max speeds of all online wheels
            internal int inTheAir; // Count of wheels in the air
            internal int operable; // Count of operable wheels
            internal int damaged; // Count of damaged wheels
            internal int online; // Count of online wheels
            internal float maxWheelRadius; // Maximum of radii of all aplicable wheels

            internal WheelTestResult(double powerRequired, double maxSpeedSum, int inTheAir, int operable, int damaged, int online, float maxWheelRadius)
            {
                this.powerRequired = powerRequired;
                this.maxSpeedSum = maxSpeedSum;
                this.inTheAir = inTheAir;
                this.operable = operable;
                this.damaged = damaged;
                this.online = online;
                this.maxWheelRadius = maxWheelRadius;
            }
        }


        /// <summary>
        /// Test stock wheels implementing standard module ModuleWheelBase
        /// </summary>
        /// <returns></returns>
        private WheelTestResult CheckStockWheels()
        {
            double powerRequired = 0;
            double maxSpeedSum = 0;
            int inTheAir = 0;
            int operable = 0;
            int damaged = 0;
            int online = 0;
            float maxWheelRadius = 0;

            // Get wheel modules
            List<Part> wheels = new List<Part>();
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                var part = vessel.parts[i];
                if (part.Modules.Contains("ModuleWheelBase"))
                    wheels.Add(part);
            }

            for (int i = 0; i < wheels.Count; i++)
            {
                ModuleWheelBase wheelBase = wheels[i].FindModuleImplementing<ModuleWheelBase>();

                // Skip legs
                if (wheelBase.wheelType == WheelType.LEG)
                    continue;

                // Save max wheel radius for height compensations
                if (wheelBase.radius < maxWheelRadius)
                    maxWheelRadius = wheelBase.radius;

                // Check damaged wheels
                ModuleWheels.ModuleWheelDamage wheelDamage = wheels[i].FindModuleImplementing<ModuleWheels.ModuleWheelDamage>();
                if (wheelDamage != null)
                {
                    if (wheelDamage.isDamaged)
                    {
                        damaged++;
                        continue;
                    }
                }

                // Whether or not wheel is touching the ground
                if (!wheelBase.isGrounded)
                {
                    inTheAir++;
                    continue;
                }
                else
                    operable++;

                // Check motorized wheels
                ModuleWheels.ModuleWheelMotor wheelMotor = wheels[i].FindModuleImplementing<ModuleWheels.ModuleWheelMotor>();
                if (wheelMotor != null)
                {
                    // Wheel is on
                    if (wheelMotor.motorEnabled)
                    {
                        powerRequired += wheelMotor.avgResRate;
                        online++;
                        double maxWheelSpeed = 0;

                        // RoveMax M1 and RoveMax M1-F (Making History expansion) don't have max wheels speed defined, so we just set it to something sensible
                        if ((wheelMotor.part.name == "roverWheel1") || (wheelMotor.part.name == "roverWheelM1-F "))
                            maxWheelSpeed = 42;
                        else
                            maxWheelSpeed = wheelMotor.wheelSpeedMax;
                        maxSpeedSum += maxWheelSpeed;
                    }
                }
            }

            return new WheelTestResult(powerRequired, maxSpeedSum, inTheAir, operable, damaged, online, maxWheelRadius);
        }


        /// <summary>
        /// Test KSPWheels implementing module KSPWheelBase
        /// </summary>
        /// <returns></returns>
        private WheelTestResult CheckKSPWheels()
        {
            double powerRequired = 0;
            double maxSpeedSum = 0;
            int inTheAir = 0;
            int operable = 0;
            int damaged = 0;
            int online = 0;
            float maxWheelRadius = 0;

            // Get wheel modules
            List<Part> wheels = new List<Part>();
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                var part = vessel.parts[i];
                if (part.Modules.Contains("KSPWheelBase"))
                    wheels.Add(part);
            }

            for (int i = 0; i < wheels.Count; i++)
            {
                List<PartModule> partModules = wheels[i].Modules.GetModules<PartModule>();
                PartModule wheelBase = partModules.Find(t => t.moduleName == "KSPWheelBase");

                // Save max wheel radius for height compensations
                float radius = (float)wheelBase.Fields.GetValue("wheelRadius");
                if (radius < maxWheelRadius)
                    maxWheelRadius = radius;

                // Check damaged wheels
                if (wheelBase.Fields.GetValue("persistentState").ToString() == "BROKEN")
                {
                    damaged++;
                    continue;
                }

                // Whether or not wheel is touching the ground
                PartModule wheelDamage = partModules.Find(t => t.moduleName == "KSPWheelDamage");
                float maxSafeSpeed = 0f; // We compare this value later with maxDrivenSpeed to eliminate unreal gear ratios
                if (wheelDamage != null)
                {
                    if ((float)wheelDamage.Fields.GetValue("loadStress") == 0)
                    {
                        inTheAir++;
                        continue;
                    }
                    else
                    {
                        operable++;
                        maxSafeSpeed = (float)wheelDamage.Fields.GetValue("maxSafeSpeed");
                    }
                }

                // Check motorized wheels
                PartModule wheelMotor = partModules.Find(t => t.moduleName == "KSPWheelMotor");
                if (wheelMotor != null)
                {
                    // Wheel is on
                    if (!(bool)wheelMotor.Fields.GetValue("motorLocked"))
                    {
                        online++;
                        powerRequired += (float)wheelMotor.Fields.GetValue("maxECDraw") * (float)wheelMotor.Fields.GetValue("motorOutput") / 100; // Motor output can be limited by a slider
                        if (maxSafeSpeed > 0)
                            maxSpeedSum += Math.Min((float)wheelMotor.Fields.GetValue("maxDrivenSpeed"), maxSafeSpeed);
                        else
                            maxSpeedSum += (float)wheelMotor.Fields.GetValue("maxDrivenSpeed");
                    }
                }

                // Check tracks
                PartModule wheelTracks = partModules.Find(t => t.moduleName == "KSPWheelTracks");
                if (wheelTracks != null)
                {
                    operable++; // We will count them as two wheels, so add another operable wheel
                    if (!(bool)wheelTracks.Fields.GetValue("motorLocked"))
                    {
                        online += 2;
                        powerRequired += (float)wheelTracks.Fields.GetValue("maxECDraw") * (float)wheelMotor.Fields.GetValue("motorOutput") / 100; // Motor output can be limited by a slider
                        if (maxSafeSpeed > 0)
                            maxSpeedSum += 2 * Math.Min((float)wheelMotor.Fields.GetValue("maxDrivenSpeed"), maxSafeSpeed);
                        else
                            maxSpeedSum += 2 * (float)wheelMotor.Fields.GetValue("maxDrivenSpeed");
                    }
                }
            }

            return new WheelTestResult(powerRequired, maxSpeedSum, inTheAir, operable, damaged, online, maxWheelRadius);
        }

        #endregion


        #region Power

        /// <summary>
        /// Calculate available power from solar panels
        /// </summary>
        /// <returns></returns>
        private double GetAvailablePower_Solar()
        {
            //// Revision - Kopernicus
            double solarPower = 0;
            double distanceToSun = vessel.distanceToSun;
            double solarFlux = PhysicsGlobals.SolarLuminosity / (4 * Math.PI * distanceToSun * distanceToSun); // f = L / SA = L / 4π r2 (Wm-2)
            float multiplier = 1;

            for (int i = 0; i < vessel.parts.Count; ++i)
            {
                ModuleDeployableSolarPanel solarPanel = vessel.parts[i].FindModuleImplementing<ModuleDeployableSolarPanel>();
                if (solarPanel == null)
                    continue;

                if ((solarPanel.deployState != ModuleDeployablePart.DeployState.BROKEN) && (solarPanel.deployState != ModuleDeployablePart.DeployState.RETRACTED) && (solarPanel.deployState != ModuleDeployablePart.DeployState.RETRACTING))
                {
                    if (solarPanel.useCurve) // Power curve
                        multiplier = solarPanel.powerCurve.Evaluate((float)distanceToSun);
                    else // solar flux at current distance / solar flux at 1AU (Kerbin)
                        multiplier = (float)(solarFlux / PhysicsGlobals.SolarLuminosityAtHome);
                    solarPower += solarPanel.chargeRate * multiplier;
                }
            }

            return solarPower;
        }


        /// <summary>
        /// Calculate available power from generators and reactors
        /// </summary>
        /// <returns></returns>
        private double GetAvailablePower_Other()
        {
            double otherPower = 0;

            // Go through all parts and get power from generators and reactors
            for (int i = 0; i < vessel.parts.Count; ++i)
            {
                var part = vessel.parts[i];

                // Standard RTG
                ModuleGenerator powerModule = part.FindModuleImplementing<ModuleGenerator>();
                if (powerModule != null)
                {
                    if (powerModule.generatorIsActive || powerModule.isAlwaysActive)
                    {
                        // Go through resources and get EC power
                        for (int j = 0; j < powerModule.resHandler.outputResources.Count; ++j)
                        {
                            var resource = powerModule.resHandler.outputResources[j];
                            if (resource.name == "ElectricCharge")
                                otherPower += resource.rate * powerModule.efficiency;
                        }
                    }
                }

                //// Revision - NF, Interstellar
                // Other generators
                PartModuleList modules = part.Modules;
                for (int j = 0; j < modules.Count; ++j)
                {
                    var module = modules[j];

                    // Near future fission reactors
                    if (module.moduleName == "FissionGenerator")
                        otherPower += double.Parse(module.Fields.GetValue("CurrentGeneration").ToString());

                    // KSP Interstellar generators
                    if (module.moduleName == "FNGenerator")
                    {
                        if (bool.Parse(module.Fields.GetValue("IsEnabled").ToString()))
                            otherPower += double.Parse(module.Fields.GetValue("maxElectricdtps").ToString());
                    }
                }
                
                // WBI reactors, USI reactors and MKS Power Pack
                ModuleResourceConverter converterModule = part.FindModuleImplementing<ModuleResourceConverter>();
                if (converterModule != null)
                {
                    if (converterModule.ModuleIsActive()
                        && ((converterModule.ConverterName == "Nuclear Reactor") || (converterModule.ConverterName == "Reactor") || (converterModule.ConverterName == "Generator")))
                    {
                        for (int j = 0; j < converterModule.outputList.Count; ++j)
                        {
                            var resource = converterModule.outputList[j];
                            if (resource.ResourceName == "ElectricCharge")
                                otherPower += resource.Ratio * converterModule.GetEfficiencyMultiplier();
                        }
                    }
                }
            }

            return otherPower;
        }

        #endregion


        /// <summary>
        /// Activate autopilot
        /// </summary>
        internal override bool Activate()
        {
            if (vessel.situation != Vessel.Situations.LANDED)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BV_Warning_Landed"), 5f).color = Color.yellow;
                return false;
            }

            SystemCheck();

            // No driving until at least 3 operable wheels are touching the ground - tricycles are allowed
            if ((wheelTestResult.inTheAir > 0) && (wheelTestResult.operable < 3))
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BV_Warning_WheelsNotTouching"), 5f).color = Color.yellow;
                return false;
            }
            if (wheelTestResult.operable < 3)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BV_Warning_WheelsNotOperable"), 5f).color = Color.yellow;
                return false;
            }

            // At least 2 wheels must be on
            if (wheelTestResult.online < 2)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BV_Warning_WheelsNotOnline"), 5f).color = Color.yellow;
                return false;
            }

            // Power production
            if (requiredPower > (electricPower_Solar + electricPower_Other))
            {
                // If required power is greater then total power generated, then average speed can be lowered up to 75%
                double speedReduction = (requiredPower - (electricPower_Solar + electricPower_Other)) / requiredPower;

                if (speedReduction > 0.75)
                {
                    ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BV_Warning_LowPower"), 5f).color = Color.yellow;
                    return false;
                }
            }

            BonVoyageModule module = vessel.FindPartModuleImplementing<BonVoyageModule>();
            if (module != null)
            {
                //vesselHeightFromTerrain = vessel.heightFromTerrain + wheelTestResult.maxWheelRadius;
                vesselHeightFromTerrain = vessel.radarAltitude;

                module.averageSpeed = averageSpeed;
                module.averageSpeedAtNight = averageSpeedAtNight;
                module.manned = manned;
                module.vesselHeightFromTerrain = vesselHeightFromTerrain;
            }

            return base.Activate();
        }


        /// <summary>
        /// Deactivate autopilot
        /// </summary>
        internal override bool Deactivate()
        {
            SystemCheck();
            return base.Deactivate();
        }


        /// <summary>
        /// Update vessel
        /// </summary>
        /// <param name="currentTime"></param>
        internal override void Update(double currentTime)
        {
            if (vessel == null)
                return;
            if (vessel.isActiveVessel)
            {
                lastTimeUpdated = 0;
                if (active)
                    ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BV_AutopilotActive"), 10f).color = Color.red;
                return;
            }
            
            if (!active || vessel.loaded)
                return;

            // If we don't know last time of update, then set it and wait for the next update cycle
            if (lastTimeUpdated == 0)
            {
                State = VesselState.Idle;
                lastTimeUpdated = currentTime;
                BVModule.SetValue("lastTimeUpdated", currentTime.ToString());
                return;
            }

            //// Revision - Kopernicus
            Vector3d roverPos = vessel.mainBody.position - vessel.GetWorldPos3D();
            Vector3d toMainStar = vessel.mainBody.position - FlightGlobals.Bodies[mainStarIndex].position;
            angle = Vector3d.Angle(roverPos, toMainStar); // Angle between rover and main star

            // Speed penalties at twighlight and at night
            if ((angle > 90) && manned) // night
                speedMultiplier = 0.25;
            else if ((angle > 85) && manned) // twilight
                speedMultiplier = 0.5;
            else if ((angle > 80) && manned) // twilight
                speedMultiplier = 0.75;
            else // day
                speedMultiplier = 1.0;

            // No moving at night, if there isn't enough power
            if ((angle > 90) && (averageSpeedAtNight == 0.0))
            {
                State = VesselState.AwaitingSunlight;
                lastTimeUpdated = currentTime;
                BVModule.SetValue("lastTimeUpdated", currentTime.ToString());
                return;
            }

            double deltaT = currentTime - lastTimeUpdated;
            double deltaS = AverageSpeed * deltaT;
            distanceTravelled += deltaS;

            if (distanceTravelled >= distanceToTarget) // We reached the target
            {
                if (!MoveSafely(targetLatitude, targetLongitude))
                    distanceTravelled -= deltaS;
                else
                {
                    distanceTravelled = distanceToTarget;

                    active = false;
                    arrived = true;
                    BVModule.SetValue("active", "False");
                    BVModule.SetValue("arrived", "True");
                    BVModule.SetValue("distanceTravelled", distanceToTarget.ToString());
                    BVModule.SetValue("pathEncoded", "");

                    // Dewarp
                    if (Configuration.AutomaticDewarp)
                    {
                        if (TimeWarp.CurrentRate > 3) // Instant drop to 50x warp
                            TimeWarp.SetRate(3, true);
                        if (TimeWarp.CurrentRate > 0) // Gradual drop out of warp
                            TimeWarp.SetRate(0, false);
                        ScreenMessages.PostScreenMessage(vessel.vesselName + " " + Localizer.Format("#LOC_BV_VesselArrived") + " " + vessel.mainBody.bodyDisplayName.Replace("^N", ""), 5f);
                    }

                    NotifyArrival();
                }
                State = VesselState.Idle;
            }
            else
            {
                int step = Convert.ToInt32(Math.Floor(distanceTravelled / PathFinder.StepSize)); // In which step of the path we are
                double remainder = distanceTravelled % PathFinder.StepSize; // Current remaining distance from the current step
                double bearing = 0;

                if (step < path.Count - 1)
                    bearing = GeoUtils.InitialBearing( // Bearing to the next step from previous step
                        path[step].latitude,
                        path[step].longitude,
                        path[step + 1].latitude,
                        path[step + 1].longitude
                    );
                else
                    bearing = GeoUtils.InitialBearing( // Bearing to the target from previous step
                        path[step].latitude,
                        path[step].longitude,
                        targetLatitude,
                        targetLongitude
                    );

                // Compute new coordinates, we are moving from the current step, length f distance is "remainder"
                double[] newCoordinates = GeoUtils.GetLatitudeLongitude(
                    path[step].latitude,
                    path[step].longitude,
                    bearing,
                    remainder,
                    vessel.mainBody.Radius
                );

                // Move
                if (!MoveSafely(newCoordinates[0], newCoordinates[1]))
                {
                    distanceTravelled -= deltaS;
                    State = VesselState.Idle;
                }
                else
                    State = VesselState.Moving;
            }

            Save(currentTime);
        }


        /// <summary>
        /// Save move of a rover. We need to prevent hitting an active vessel.
        /// </summary>
        /// <param name="latitude"></param>
        /// <param name="longitude"></param>
        /// <returns>true if rover was moved, false otherwise</returns>
        private bool MoveSafely(double latitude, double longitude)
        {
            double altitude = GeoUtils.TerrainHeightAt(latitude, longitude, vessel.mainBody);
            if (FlightGlobals.ActiveVessel != null)
            {
                Vector3d newPos = vessel.mainBody.GetWorldSurfacePosition(latitude, longitude, altitude);
                Vector3d actPos = FlightGlobals.ActiveVessel.GetWorldPos3D();
                double distance = Vector3d.Distance(newPos, actPos);
                if (distance <= 2400)
                    return false;
            }

            vessel.latitude = latitude;
            vessel.longitude = longitude;
            vessel.altitude = altitude + vesselHeightFromTerrain;

            return true;
        }


        /// <summary>
        /// Notify, that rover has arrived
        /// </summary>
        private void NotifyArrival()
        {
            MessageSystem.Message message = new MessageSystem.Message(
                "Rover arrived", // title
                "<color=#74B4E2>" + vessel.vesselName + "</color> " + Localizer.Format("#LOC_BV_VesselArrived") + " " + vessel.mainBody.bodyDisplayName.Replace("^N", "") + ".\n<color=#AED6EE>"
                + Localizer.Format("#LOC_BV_Control_Lat") + ": " + targetLatitude.ToString("F2") + "</color>\n<color=#AED6EE>" + Localizer.Format("#LOC_BV_Control_Lon") + ": " + targetLongitude.ToString("F2") + "</color>", // message
                MessageSystemButton.MessageButtonColor.GREEN,
                MessageSystemButton.ButtonIcons.COMPLETE
            );
            MessageSystem.Instance.AddMessage(message);
        }

    }

}
