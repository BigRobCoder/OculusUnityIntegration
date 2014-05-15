/************************************************************************************

Filename    :   OVRDevice.cs
Content     :   Interface for the Oculus Rift Device
Created     :   February 14, 2013
Authors     :   Peter Giokaris

Copyright   :   Copyright 2013 Oculus VR, Inc. All Rights reserved.

Use of this software is subject to the terms of the Oculus LLC license
agreement provided at the time of installation or download, or which
otherwise accompanies this software in either electronic or hard copy form.

************************************************************************************/
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

//-------------------------------------------------------------------------------------
// ***** OVRDevice
//
// OVRDevice is the main interface to the Oculus Rift hardware. It includes wrapper functions
// for  all exported C++ functions, as well as helper functions that use the stored Oculus
// variables to help set up camera behavior.
//
// This component is added to the OVRCameraController prefab. It can be part of any 
// game object that one sees fit to place it. However, it should only be declared once,
// since there are public members that allow for tweaking certain Rift values in the
// Unity inspector.
//
public class OVRDevice : MonoBehaviour 
{
	// MessageList
	[StructLayout(LayoutKind.Sequential)]
	public struct MessageList
	{
		public byte isHMDSensorAttached;
		public byte isHMDAttached;
		public byte isLatencyTesterAttached;
		
		public MessageList(byte HMDSensor, byte HMD, byte LatencyTester)
		{
			isHMDSensorAttached = HMDSensor;
			isHMDAttached = HMD;
			isLatencyTesterAttached = LatencyTester;
		}
	}
	
	// Different orientations required for different trackers
	enum SensorOrientation {Head, Other};
	
	// PUBLIC
	public float InitialPredictionTime 							= 0.05f; // 50 ms
	public float InitialAccelGain  								= 0.05f; // default value
	public bool  ResetTracker									= true;  // if off, tracker will not reset when new scene
																		 // is loaded
	// STATIC
	private static MessageList MsgList 							= new MessageList(0, 0, 0);
	private static bool  OVRInit 								= false;

	public static int    SensorCount 					 	    = 0;
	
	public static String DisplayDeviceName;
	
	public static int    HResolution, VResolution 				= 0;	 // pixels
	public static float  HScreenSize, VScreenSize 				= 0.0f;	 // meters
	public static float  EyeToScreenDistance  					= 0.0f;  // meters
	public static float  LensSeparationDistance 				= 0.0f;  // meters
	public static float  LeftEyeOffset, RightEyeOffset			= 0.0f;  // meters
	public static float  ScreenVCenter 							= 0.0f;	 // meters 
	public static float  DistK0, DistK1, DistK2, DistK3 		= 0.0f;
	
	// Used to reduce the size of render distortion and give better fidelity
	public static float  DistortionFitScale 					= 1.0f;  	
	
	// The physical offset of the lenses, used for shifting both IPD and lens distortion
	private static float LensOffsetLeft, LensOffsetRight   		= 0.0f;
	
	// Fit to top of the image (default is 5" display)
    private static float DistortionFitX 						= 0.0f;
    private static float DistortionFitY 						= 1.0f;
	
	// Copied from initialized public variables set in editor
	private static float PredictionTime 						= 0.0f;
	private static float AccelGain 								= 0.0f;
	
	// We will keep map sensors to different numbers to know which sensor is 
	// attached to which device
	private static Dictionary<int,int> SensorList = new Dictionary<int, int>(); 
	private static Dictionary<int,SensorOrientation> SensorOrientationList = 
			   new Dictionary<int, SensorOrientation>(); 
	
	// List of functions to call on Init_Callback
	private static List<Action> pendingInits = new List<Action>();

	// * * * * * * * * * * * * *

	// Awake
	void Awake () 
	{	
		// Initialize static Dictionary lists first
		InitSensorList(false); 
		InitOrientationSensorList();
		
		Application.ExternalEval("OVR_Init()");
	}
	void Init_Callback(string paramsStr)
	{
		OVRInit = true;

		// * * * * * * *
		// DISPLAY SETUP
		
		string[] paramArray = paramsStr.Split('|');

		// We will get the HMD so that we can eventually target it within Unity
		DisplayDeviceName += paramArray[0];

		HResolution            = int.Parse(paramArray[1]);
		VResolution            = int.Parse(paramArray[2]);
		HScreenSize            = float.Parse(paramArray[3]);
		VScreenSize            = float.Parse(paramArray[4]);
		ScreenVCenter          = float.Parse(paramArray[5]);
		EyeToScreenDistance    = float.Parse(paramArray[6]);
		LensSeparationDistance = float.Parse(paramArray[7]);
		DistK0                 = float.Parse(paramArray[8]);
		DistK1                 = float.Parse(paramArray[9]);
		DistK2                 = float.Parse(paramArray[10]);
		DistK3                 = float.Parse(paramArray[11]);
#if TO_BE_IMPLEMENTED
		LeftEyeOffset          = float.Parse(paramArray[12]);
		RightEyeOffset         = float.Parse(paramArray[13]);
		SensorCount            = int.Parse(paramArray[14]);
#else
		SensorCount            = 2;
#endif
		
		// Distortion fit parameters based on if we are using a 5" (Prototype, DK2+) or 7" (DK1) 
		if (HScreenSize < 0.140f) 	// 5.5"
		{
			DistortionFitX 		= 0.0f;
			DistortionFitY 		= 1.0f;
		}
    	else 						// 7" (DK1)
		{
			DistortionFitX 		= -1.0f;
			DistortionFitY 		=  0.0f;
			DistortionFitScale 	=  0.7f;
		}
		
		// Calculate the lens offsets for each eye and store 
		CalculatePhysicalLensOffsets(ref LensOffsetLeft, ref LensOffsetRight);
		
		// * * * * * * *
		// SENSOR SETUP
		
		// PredictionTime set, to init sensor directly
		if(PredictionTime > 0.0f)
			Application.ExternalEval("OVR_SetSensorPredictionTime(" + SensorList[0] + "|" + PredictionTime + ")");
		else
			SetPredictionTime(SensorList[0], InitialPredictionTime);	
		
		// AcelGain set, used to correct gyro with accel. 
		// Default value is appropriate for typical use.
		if(AccelGain > 0.0f)
            Application.ExternalEval("OVR_SetSensorAccelGain(" + SensorList[0] + "|" + AccelGain + ")");
		else
			SetAccelGain(SensorList[0], InitialAccelGain);	

		// Kick off any pending inits
		foreach (Action init in pendingInits)
		{
			init();
		}
		pendingInits.Clear();
	}

	public static void CallWhenInitDone(Action action)
	{
		if (OVRInit)
			action();
		else
			pendingInits.Add(action);
	}
   
	// Start (Note: make sure to always have a Start function for classes that have
	// editors attached to them)
	void Start()
	{
	}
	
	// Update
	// We can detect if our devices have been plugged or unplugged, as well as
	// run things that need to be updated in our game thread
	void Update()
	{	
		Application.ExternalEval("OVR_Update()");
	}
	void Update_Callback(string paramsStr)
	{
		MessageList oldMsgList = MsgList;

		string[] paramArray = paramsStr.Split('|');
		MsgList.isHMDSensorAttached     = byte.Parse(paramArray[0]);
		MsgList.isHMDAttached           = byte.Parse(paramArray[1]);
		MsgList.isLatencyTesterAttached = byte.Parse(paramArray[2]);
		
		// HMD SENSOR
		if((MsgList.isHMDSensorAttached != 0) && 
		   (oldMsgList.isHMDSensorAttached == 0))
		{
			OVRMessenger.Broadcast<OVRMainMenu.Device, bool>("Sensor_Attached", OVRMainMenu.Device.HMDSensor, true); 
			//Debug.Log("HMD SENSOR ATTACHED");
		}
		else if((MsgList.isHMDSensorAttached == 0) && 
		   (oldMsgList.isHMDSensorAttached != 0))
		{
			OVRMessenger.Broadcast<OVRMainMenu.Device, bool>("Sensor_Attached", OVRMainMenu.Device.HMDSensor, false);
			//Debug.Log("HMD SENSOR DETACHED");
		}

		// HMD
		if((MsgList.isHMDAttached != 0) && 
		   (oldMsgList.isHMDAttached == 0))
		{
			OVRMessenger.Broadcast<OVRMainMenu.Device, bool>("Sensor_Attached", OVRMainMenu.Device.HMD, true); 
			//Debug.Log("HMD ATTACHED");
		}
		else if((MsgList.isHMDAttached == 0) && 
		   (oldMsgList.isHMDAttached != 0))
		{
			OVRMessenger.Broadcast<OVRMainMenu.Device, bool>("Sensor_Attached", OVRMainMenu.Device.HMD, false); 
			//Debug.Log("HMD DETACHED");
		}

		// LATENCY TESTER
		if((MsgList.isLatencyTesterAttached != 0) && 
		   (oldMsgList.isLatencyTesterAttached == 0))
		{
			OVRMessenger.Broadcast<OVRMainMenu.Device, bool>("Sensor_Attached", OVRMainMenu.Device.LatencyTester, true); 
			//Debug.Log("LATENCY TESTER ATTACHED");
		}
		else if((MsgList.isLatencyTesterAttached == 0) && 
		   (oldMsgList.isLatencyTesterAttached != 0))
		{
			OVRMessenger.Broadcast<OVRMainMenu.Device, bool>("Sensor_Attached", OVRMainMenu.Device.LatencyTester, false); 
			//Debug.Log("LATENCY TESTER DETACHED");
		}
	}
		
	// OnDestroy
	void OnDestroy()
	{
#if TO_BE_IMPLEMENTED
		// We may want to turn this off so that values are maintained between level / scene loads
		if(ResetTracker == true)
		{
			OVR_Destroy();
			OVRInit = false;
		}
#endif
	}
	
	
	// * * * * * * * * * * * *
	// PUBLIC FUNCTIONS
	// * * * * * * * * * * * *
	
	// Inited - Check to see if system has been initialized
	public static bool IsInitialized()
	{
		return OVRInit;
	}
	
	// HMDPreset
	public static bool IsHMDPresent()
	{
#if TO_BE_IMPLEMENTED
		return OVR_IsHMDPresent();
#else
		return false;
#endif
	}

	// SensorPreset
	public static bool IsSensorPresent(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_IsSensorPresent(SensorList[sensor]);
#else
		return false;
#endif
	}

	// GetOrientation
	public static void GetOrientation(int sensor, string callback)
	{
		Application.ExternalEval("OVR_GetSensorOrientation(" + SensorList[sensor] + "|" + false + "|" + callback + ")");
	}
	
	// GetPredictedOrientation
	public static void GetPredictedOrientation(int sensor, string callback)
	{
		Application.ExternalEval("OVR_GetSensorOrientation(" + SensorList[sensor] + "|" + true + "|" + callback + ")");
	}
	
	// GetOrientation_Callback
	public static bool GetOrientation_Callback(string paramsStr, ref Quaternion q)
	{
		string[] paramArray = paramsStr.Split('|');
		int sensor = int.Parse(paramArray[0]);
		if(bool.Parse(paramArray[1]))
		{
			if(sensor == 0)
			{
				q.w = float.Parse(paramArray[2]);
				q.x = float.Parse(paramArray[3]);
				q.y = float.Parse(paramArray[4]);
				q.z = float.Parse(paramArray[5]);
				OrientSensor(sensor, ref q);

				return true;
			}
		}
		
		return false;
	}
	
	// ResetOrientation
	public static bool ResetOrientation(int sensor)
	{
#if TO_BE_IMPLEMENTED
        return OVR_ResetSensorOrientation(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// GetPredictionTime
	public static float GetPredictionTime(int sensor)
	{		
		// return OVRSensorsGetPredictionTime(sensor, ref predictonTime);
		return PredictionTime;
	}

	// SetPredictionTime
	public static bool SetPredictionTime(int sensor, float predictionTime)
	{
#if TO_BE_IMPLEMENTED
		if ( (predictionTime > 0.0f) &&
             (OVR_SetSensorPredictionTime(SensorList[sensor], predictionTime) == true))
		{
			PredictionTime = predictionTime;
			return true;
		}
		
#endif
		return false;
	}
	
	// GetAccelGain
	public static float GetAccelGain(int sensor)
	{		
		return AccelGain;
	}

	// SetAccelGain
	public static bool SetAccelGain(int sensor, float accelGain)
	{
#if TO_BE_IMPLEMENTED
		if ( (accelGain > 0.0f) &&
             (OVR_SetSensorAccelGain(SensorList[sensor], accelGain) == true))
		{
			AccelGain = accelGain;
			return true;
		}
		
#endif
		return false;
	}
	
	// GetDistortionCorrectionCoefficients
	public static bool GetDistortionCorrectionCoefficients(ref float k0, 
														   ref float k1, 
														   ref float k2, 
														   ref float k3)
	{
		if(!OVRInit)
			return false;
		
		k0 = DistK0;
		k1 = DistK1;
		k2 = DistK2;
		k3 = DistK3;
		
		return true;
	}
	
	// SetDistortionCorrectionCoefficients
	public static bool SetDistortionCorrectionCoefficients(float k0, 
														   float k1, 
														   float k2, 
														   float k3)
	{
		if(!OVRInit)
			return false;
		
		DistK0 = k0;
		DistK1 = k1;
		DistK2 = k2;
		DistK3 = k3;
		
		return true;
	}
	
	// GetPhysicalLensOffsets
	public static bool GetPhysicalLensOffsets(ref float lensOffsetLeft, 
											  ref float lensOffsetRight)
	{
		if(!OVRInit)
			return false;
		
		lensOffsetLeft  = LensOffsetLeft;
		lensOffsetRight = LensOffsetRight;	
		
		return true;
	}
	
	// GetIPD
	public static bool GetIPD(ref float IPD)
	{
#if TO_BE_IMPLEMENTED
		if(!OVRInit)
			return false;

		OVR_GetInterpupillaryDistance(ref IPD);
		
		return true;
#else
		return false;
#endif
	}
		
	// CalculateAspectRatio
	public static float CalculateAspectRatio()
	{
		if(Application.isEditor)
			return (Screen.width * 0.5f) / Screen.height;
		else
			return (HResolution * 0.5f) / VResolution;		
	}
	
	// VerticalFOV
	// Compute Vertical FOV based on distance, distortion, etc.
    // Distance from vertical center to render vertical edge perceived through the lens.
    // This will be larger then normal screen size due to magnification & distortion.
	public static float VerticalFOV()
	{
		if(!OVRInit)
		{
			return 90.0f;
		}
			
    	float percievedHalfScreenDistance = (VScreenSize / 2) * DistortionScale();
    	float VFov = Mathf.Rad2Deg * 2.0f * 
			         Mathf.Atan(percievedHalfScreenDistance / EyeToScreenDistance);	
		
		return VFov;
	}
	
	// DistortionScale - Used to adjust size of shader based on 
	// shader K values to maximize screen size
	public static float DistortionScale()
	{
		if(OVRInit)
		{
			float ds = 0.0f;
		
			// Compute distortion scale from DistortionFitX & DistortionFitY.
    		// Fit value of 0.0 means "no fit".
    		if ((Mathf.Abs(DistortionFitX) < 0.0001f) &&  (Math.Abs(DistortionFitY) < 0.0001f))
    		{
        		ds = 1.0f;
    		}
    		else
    		{
        		// Convert fit value to distortion-centered coordinates before fit radius
        		// calculation.
        		float stereoAspect = 0.5f * Screen.width / Screen.height;
        		float dx           = (DistortionFitX * DistortionFitScale) - LensOffsetLeft;
        		float dy           = (DistortionFitY * DistortionFitScale) / stereoAspect;
        		float fitRadius    = Mathf.Sqrt(dx * dx + dy * dy);
        		ds  			   = CalcScale(fitRadius);
    		}	
			
			if(ds != 0.0f)
				return ds;
			
		}
		
		return 1.0f; // no scale
	}
	
	// LatencyProcessInputs
    public static void ProcessLatencyInputs()
	{
#if TO_BE_IMPLEMENTED
        OVR_ProcessLatencyInputs();
#endif
	}
	
	// LatencyProcessInputs
    public static bool DisplayLatencyScreenColor(ref byte r, ref byte g, ref byte b)
	{
#if TO_BE_IMPLEMENTED
        return OVR_DisplayLatencyScreenColor(ref r, ref g, ref b);
#else
		return false;
#endif
	}
	
	// LatencyGetResultsString
    public static System.IntPtr GetLatencyResultsString()
	{
#if TO_BE_IMPLEMENTED
        return OVR_GetLatencyResultsString();
#else
		return System.IntPtr.Zero;
#endif
	}
	
	// Computes scale that should be applied to the input render texture
    // before distortion to fit the result in the same screen size.
    // The 'fitRadius' parameter specifies the distance away from distortion center at
    // which the input and output coordinates will match, assuming [-1,1] range.
    public static float CalcScale(float fitRadius)
    {
        float s = fitRadius;
        // This should match distortion equation used in shader.
        float ssq   = s * s;
        float scale = s * (DistK0 + DistK1 * ssq + DistK2 * ssq * ssq + DistK3 * ssq * ssq * ssq);
        return scale / fitRadius;
    }
	
	// CalculatePhysicalLensOffsets - Used to offset perspective and distortion shift
	public static bool CalculatePhysicalLensOffsets(ref float leftOffset, ref float rightOffset)
	{
		leftOffset  = 0.0f;
		rightOffset = 0.0f;
		
		if(!OVRInit)
			return false;
		
		float halfHSS = HScreenSize * 0.5f;
		float halfLSD = LensSeparationDistance * 0.5f;
		
		leftOffset =  (((halfHSS - halfLSD) / halfHSS) * 2.0f) - 1.0f;
		rightOffset = (( halfLSD / halfHSS) * 2.0f) - 1.0f;
		
		return true;
	}
	
	// MAG YAW-DRIFT CORRECTION FUNCTIONS
	
	// AUTO MAG CALIBRATION FUNCTIONS
	
	// BeginMagAutoCalibraton
	public static bool BeginMagAutoCalibration(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_BeginMagAutoCalibraton(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// StopMagAutoCalibraton
	public static bool StopMagAutoCalibration(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_StopMagAutoCalibraton(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// UpdateMagAutoCalibration
	public static bool UpdateMagAutoCalibration(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_UpdateMagAutoCalibration(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// MANUAL MAG CALIBRATION FUNCTIONS
	
	// BeginMagManualCalibration
	public static bool BeginMagManualCalibration(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_BeginMagManualCalibration(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// StopMagManualCalibration
	public static bool StopMagManualCalibration(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_StopMagManualCalibration(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// UpdateMagManualCalibration
	public static bool UpdateMagManualCalibration(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_UpdateMagManualCalibration(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// OVR_MagManualCalibrationState
	// 0 = Forward, 1 = Up, 2 = Left, 3 = Right, 4 = Upper-Right, 5 = FAIL, 6 = SUCCESS!
	public static int MagManualCalibrationState(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_MagManualCalibrationState(SensorList[sensor]);
#else
		return 0;
#endif
	}
	
	// SHARED MAG CALIBRATION FUNCTIONS
	
	// MagNumberOfSamples
	public static int MagNumberOfSamples(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_MagNumberOfSamples(SensorList[sensor]);
#else
		return 0;
#endif
	}

	// IsMagCalibrated
	public static bool IsMagCalibrated(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_IsMagCalibrated(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// EnableMagYawCorrection
	public static bool EnableMagYawCorrection(int sensor, bool enable)
	{
#if TO_BE_IMPLEMENTED
		return OVR_EnableMagYawCorrection(SensorList[sensor], enable);
#else
		return false;
#endif
	}
	
	// OVR_IsYawCorrectionEnabled
	public static bool IsYawCorrectionEnabled(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_IsYawCorrectionEnabled(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// IsMagYawCorrectionInProgress
	public static bool IsMagYawCorrectionInProgress(int sensor)
	{
#if TO_BE_IMPLEMENTED
		return OVR_IsMagYawCorrectionInProgress(SensorList[sensor]);
#else
		return false;
#endif
	}
	
	// InitSensorList:
	// We can remap sensors to different parts
	public static void InitSensorList(bool reverse)
	{
		SensorList.Clear();
	
		if(reverse == true)
		{
			SensorList.Add(0,1);
			SensorList.Add(1,0);
		}
		else
		{
			SensorList.Add(0,0);
			SensorList.Add(1,1);
		}
	}
	
	// InitOrientationSensorList
	public static void InitOrientationSensorList()
	{
		SensorOrientationList.Clear();
		SensorOrientationList.Add (0, SensorOrientation.Head);
		SensorOrientationList.Add (1, SensorOrientation.Other);
	}
	
	// OrientSensor
	// We will set up the sensor based on which one it is
	public static void OrientSensor(int sensor, ref Quaternion q)
	{
		if(SensorOrientationList[sensor] == SensorOrientation.Head)
		{
			// Change the co-ordinate system from right-handed to Unity left-handed
			/*
			q.x =  x; 
			q.y =  y;
			q.z =  -z; 
			q = Quaternion.Inverse(q);
			*/
			
			// The following does the exact same conversion as above
			q.x = -q.x; 
			q.y = -q.y;	

		}
		else if(SensorOrientationList[sensor] == SensorOrientation.Other)
		{	
			// Currently not used 
			float tmp = q.x;
			q.x = q.z;
			q.y = -q.y;
			q.z = tmp;
		}
	}
	
	// RenderPortraitMode
	public static bool RenderPortraitMode()
	{
#if TO_BE_IMPLEMENTED
		return OVR_RenderPortraitMode();
#else
		return false;
#endif
	}
	
	// GetPlayerEyeHeight
	public static bool GetPlayerEyeHeight(ref float eyeHeight)
	{
#if TO_BE_IMPLEMENTED
		return OVR_GetPlayerEyeHeight(ref eyeHeight);
#else
		return false;
#endif
	}
}
