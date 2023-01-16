using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine;
using KModkit;

public class VentilationScript : MonoBehaviour {

	public KMBombInfo Bomb;
	public KMAudio Audio;
	public KMBombModule Module;
	public KMBossModule BossInfo;

	public Material[] LightColors;//Black, Orange, green, red
	public KMSelectable[] switches;
	public KMSelectable lockSwitch;
	public Renderer[] switchLables;
	public Material[] lableMATS;
	public Transform[] switchFuseTransforms;
	public Renderer[] switchLights;
	public Renderer statusLight;

	public KMSelectable submitLever;
	public Transform submitLeverObject;
	public Transform FanObject;
	//public KMSelectable submitButton;
	//public KMSelectable[] toggleButtons;

	//-----------------------------------------------------//
	//READONLY LIBRARIES
	
	private bool[] switchVal = {false, false, false, false};
	private bool[] switchHOLD = {false, false, false};
	private bool[] switchLAG = {false, false, false, false};
	private int[] switchLagFRAMES = {0, 0, 0, 0};

	private bool TPsolveLag = false;


	private int errorFRAME = 0;
	private int errorLAG = 0;
	private List<int> errorSET = new List<int> {};
	private int[] errorVAL = {0, 0, 0};

	private int[] stepVALS = {0, 0};

	private int currentStep = 0;
	private int[] errorToStep = {0, 3, 6, 9};
	private int[,] stepToPath = {
		//False, True, Set
		{1, 12, 2},
		{2, 13, 3},
		{3, 4, 0},
		{4, 13, 5},
		{5, 14, 4},
		{6, 7, 1},
		{7, 14, 7},
		{8, 15, 6},
		{9, 10, 3},
		{10, 15, 0},
		{11, 12, 1},
		{0, 1, 7},
		{13, 2, 5},
		{14, 5, 6},
		{15, 8, 2},
		{12, 11, 4},
	};

	private bool[,] stateChart = {
		{false, false, false}, // 0-0-0 // 0
		{true, false, false},  // 1-0-0 // 1
		{false, true, false},  // 0-1-0 // 2
		{false, false, true},  // 0-0-1 // 3
		{true, true, false},   // 1-1-0 // 4
		{true, false, true},   // 1-0-1 // 5
		{false, true, true},   // 0-1-1 // 6
		{true, true, true}     // 1-1-1 // 7
	};

	private int nineSTEP = 0;

	private bool[,] solutionVals = {{true, true, true}, {false, false, false}};
	//private bool[,] debugSolveVals = {{false, false, true}, {true, true, false}};

	private int[] debugOrder = {0, 0, 0, 0, 0, 0};
	private bool submitBOOL = false;
	private bool submitLAG = false;
	private int lagFrames = 0;

	private int fanLAG = 0;
	private int fanFRAME = 0;
	private float fanSpeed = 0.0f;

	int solveCount = 0;

	//-----------------------------------------------------//

	//Logging (Goes at the bottom of global variables)
	static int moduleIdCounter = 1;
	int moduleId;

	private bool cbActive;
	private bool moduleSolved = false;

	private void Awake() {
		moduleId = moduleIdCounter++;
		foreach (KMSelectable NAME in switches) {
			KMSelectable pressedObject = NAME;
			NAME.OnInteract += delegate () { FlipSwitch(pressedObject); return false; };
		}
		lockSwitch.OnInteract += delegate () { LOCKSwitch(); return false; };
		submitLever.OnInteract += delegate () { Submit(); return false; };
	}

	void Start() {
		Debug.LogFormat("[Ventilation #{0}] Power Surge in Ventilation! Power reroute required...", moduleId);
		InitSolution();
	}

	void InitSolution() {
		for (int i = 0; i < 3; i++){
			if (i == 1){errorVAL[i] = UnityEngine.Random.Range(1, 4);} else {errorVAL[i] = UnityEngine.Random.Range(1, 5);}
			for (int j = 0; j < errorVAL[i]; j++){
				errorSET.Add(1);
				errorSET.Add(0);
			}
			errorSET.Add(0);
			errorSET.Add(0);
		}
		errorSET.Add(0);
		errorSET.Add(0);
		Debug.LogFormat("[Ventilation #{0}] Error code is [{1}]-[{2}]-[{3}]", moduleId, errorVAL[0], errorVAL[1], errorVAL[2]);
		Debug.LogFormat("[Ventilation #{0}] Starting corners are [{1}] for HOLD and [{2}] for FUSE", moduleId, errorVAL[0], errorVAL[2]);

		if (Bomb.GetBatteryCount() != 0){stepVALS[0] = errorVAL[1] * Bomb.GetBatteryCount();} else {stepVALS[0] = errorVAL[1] * 6;}
		if (Bomb.GetPortCount() != 0){stepVALS[1] = errorVAL[1] * Bomb.GetPortCount();} else {stepVALS[1] = errorVAL[1] * 5;}
		Debug.LogFormat("[Ventilation #{0}] Number of steps are [{1}] for HOLD and [{2}] for FUSE", moduleId, stepVALS[0], stepVALS[1]);

		LogOUT();

		NavigateFlowchart();

		for (int i = 0; i < 3; i++){
			if (solutionVals[0, i]){debugOrder[i] = 1;} else {debugOrder[i] = 0;}
			if (solutionVals[1, i]){debugOrder[i+3] = 1;} else {debugOrder[i+3] = 0;}
		}
		Debug.LogFormat("[Ventilation #{0}] Solution is [{1}]-[{2}]-[{3}] in the HOLD and [{4}]-[{5}]-[{6}] for the fuse switches", moduleId, debugOrder[0], debugOrder[1], debugOrder[2], debugOrder[3], debugOrder[4], debugOrder[5]);
	}

	void LogOUT(){
		solveCount = Bomb.GetSolvableModuleIDs().Count();
		string[] moduleNames = Bomb.GetModuleNames().ToArray();
		bool LogBOOL = true;
		int PRIME = 0;
		for (int j = 1; j < solveCount+1; j++){
			if(solveCount % j == 0){
				PRIME += 1;
				//Debug.Log(j);
			}
			if(PRIME >= 3 || solveCount == 1){
				LogBOOL = false;
				break;
			}
			if(j == solveCount){
				LogBOOL = true;
				break;
			}
		}
		Debug.LogFormat("[Ventilation #{0}] Solvable modules is prime [{1}]", moduleId, LogBOOL);

		LogBOOL = false;
		foreach (string[] portPlate in Bomb.GetPortPlates()){
			if(portPlate.Count() == 0){
				LogBOOL = true;
				break;
			}
		}
		Debug.LogFormat("[Ventilation #{0}] Empty port plate exists [{1}]", moduleId, LogBOOL);

		LogBOOL = false;
		int totalMAZES = 0;
		PRIME = 0;
		for (int x = 0; x < moduleNames.Length; x++) {
			if (moduleNames[x].ContainsIgnoreCase("Maze")) {
				totalMAZES += 1;
				//Debug.Log(totalMAZES);
			}
		}
		for (int j = 1; j < solveCount+1; j++){
			//Debug.Log(j);
			if(totalMAZES % j == 0){
				PRIME += 1;
			}
			if(PRIME >= 3 || totalMAZES <= 1){
				LogBOOL = true;
				break;
			}
			if(j == totalMAZES){
				LogBOOL = false;
				break;
			}
		}
		Debug.LogFormat("[Ventilation #{0}] # of MAZE modules is not prime [{1}]", moduleId, LogBOOL);

		LogBOOL = false;
		if(Bomb.GetSerialNumberNumbers().Count() == 3){LogBOOL = true;}
		Debug.LogFormat("[Ventilation #{0}] Serial Number has exactly 3 digits [{1}]", moduleId, LogBOOL);

		LogBOOL = false;
		for (int x = 0; x < moduleNames.Length; x++) {
			if (moduleNames[x].ContainsIgnoreCase("Wire")) {
				LogBOOL = !LogBOOL;
			}
		}
		Debug.LogFormat("[Ventilation #{0}] Odd # of WIRE modules [{1}]", moduleId, LogBOOL);

		LogBOOL = false;
		if (BossInfo.GetIgnoredModules("Ventilation").Count() == 0){
			if(Bomb.GetSolvableModuleNames().Contains("The Generator")){LogBOOL = true;} else {LogBOOL = false;}
		} else {
		//DEBUG END
			foreach (string BossList in BossInfo.GetIgnoredModules("Forget Me Not")){
				if(Bomb.GetSolvableModuleNames().Contains(BossList)){
					LogBOOL = true;
					break;
				}
			}
		}
		Debug.LogFormat("[Ventilation #{0}] Bomb contains IGNORED MODULE [{1}]", moduleId, LogBOOL);

		LogBOOL = false;
		int cipherCount = 0;
		for (int x = 0; x < moduleNames.Length; x++) {
			if (moduleNames[x].ContainsIgnoreCase("Cipher")) {
				cipherCount += 1;
			}
		}
		if (cipherCount % 3 == 0){LogBOOL = true;}
		Debug.LogFormat("[Ventilation #{0}] CIPHER module count mod 3 is 0 [{1}]", moduleId, LogBOOL);

		LogBOOL = false;
		if(Bomb.GetOffIndicators().Count() > Bomb.GetOnIndicators().Count()){LogBOOL = true;}
		Debug.LogFormat("[Ventilation #{0}] Unlit indicators > lit indicators [{1}]", moduleId, LogBOOL);
	}

	void NavigateFlowchart() {
		Debug.LogFormat("[Ventilation #{0}] Flowchart is labeled as shown:", moduleId);
		Debug.LogFormat("[Ventilation #{0}] 01-02-03-04", moduleId);
		Debug.LogFormat("[Ventilation #{0}] 12-13-14-05", moduleId);
		Debug.LogFormat("[Ventilation #{0}] 11-16-15-06", moduleId);
		Debug.LogFormat("[Ventilation #{0}] 10-09-08-07", moduleId);
		for (int i = 0; i < 2; i++){
			nineSTEP = 0;
			if (i == 0){currentStep = errorToStep[errorVAL[0] - 1];} else {currentStep = errorToStep[errorVAL[2] - 1];}
			for (int STEP = 0; STEP < stepVALS[i]; STEP++){
				if (conditionCheck(STEP + 1)){
					currentStep = stepToPath[currentStep, 1];
				} else {
					currentStep = stepToPath[currentStep, 0];
				}
				Debug.LogFormat("[Ventilation #{0}] Set [{1}] Current [{2}] Total [{3}]", moduleId, (i+1), (currentStep+1), (STEP+1));
			}
			//Debug.Log("Ending step is" + currentStep);

			for (int j = 0; j < 3; j++){
				solutionVals[i, j] = stateChart[stepToPath[currentStep, 2], j]; // debugSolveVals[i, j];
			}
		}
	}

	bool conditionCheck(int i) {
		if (currentStep == 0){ // VERIFIED
			int PRIME = 0;
			for (int j = 1; j < solveCount+1; j++){
				if(Bomb.GetSolvableModuleIDs().Count() % j == 0){
					PRIME += 1;
				}
				if(PRIME >= 3 || Bomb.GetSolvableModuleIDs().Count() == 1){return false;}
				if(j == Bomb.GetSolvableModuleIDs().Count()){return true;}
			}
			return true;

		} else if (currentStep == 1){ // VERIFIED
			foreach (string[] portPlate in Bomb.GetPortPlates()){
				if(portPlate.Count() == 0){
					return true;
				}
			}
			return false;

		} else if (currentStep == 2){ // VERIFIED
			string[] names = Bomb.GetModuleNames().ToArray();
			int totalMAZES = 0;
			int PRIME = 0;
			for (int x = 0; x < names.Length; x++) {
				if (names[x].ContainsIgnoreCase("Maze")) {
					totalMAZES += 1;
				}
			}
			for (int j = 1; j < solveCount+1; j++){
				if(totalMAZES % j == 0){
					PRIME += 1;
				}
				if(PRIME >= 3 || totalMAZES <= 1){return true;}
				if(j == totalMAZES){return false;}
			}
			return false;// PROBLEM: If totalMAZES == 0 then it'll return false despite 0 not being prime // EDIT maybe not..?

		} else if (currentStep == 3){ // VERIFIED
			if(Bomb.GetSerialNumberNumbers().Count() == 3){
				return true;
			} else {return false;}

		} else if (currentStep == 4){ // VERIFIED
			if(i % 2 == 1){
				return true;
			} else {return false;}

		} else if (currentStep == 5){ // VERIFIED
			string[] names = Bomb.GetModuleNames().ToArray();
			bool oddSIMON = false;
			for (int x = 0; x < names.Length; x++) {
				if (names[x].ContainsIgnoreCase("Wire")) {
					oddSIMON = !oddSIMON;
				}
			}
			return oddSIMON;

		} else if (currentStep == 6){
			//DEBUG
			if (BossInfo.GetIgnoredModules("Ventilation").Count() == 0){
				if(Bomb.GetSolvableModuleNames().Contains("The Generator")){return true;} else {return false;}
			}
			//DEBUG END
			foreach (string BossList in BossInfo.GetIgnoredModules("Forget Me Not")){
				if(Bomb.GetSolvableModuleNames().Contains(BossList)){
					return true;
				}
			}
			return false;

		} else if (currentStep == 7){
			if(errorVAL[0] == errorVAL[2]){
				return true;
			} else {return false;}

		} else if (currentStep == 8){ // VERIFIED
			nineSTEP += 1;
			if(nineSTEP % 2 == 1){
				return true;
			} else {return false;}
			
		} else if (currentStep == 9){ // VERIFIED
			string[] names = Bomb.GetModuleNames().ToArray();
			int cipherCount = 0;
			for (int x = 0; x < names.Length; x++) {
				if (names[x].ContainsIgnoreCase("Cipher")) {
					cipherCount += 1;
				}
			}
			if (cipherCount % 3 == 0){return true;} else {return false;}

		} else if (currentStep == 10){ // VERIFIED
			if(i % 2 == 1){
				return true;
			} else {return false;}

		} else if (currentStep == 11){ // VERIFIED
			if(Bomb.GetOffIndicators().Count() > Bomb.GetOnIndicators().Count()){
				return true;
			} else {return false;}

		} else { // VERIFIED
			if(i % 2 == 0){
				return true;
			} else {return false;}
		} 
	}

	void FlipSwitch(KMSelectable switchObject) {//KMSelectable button
		int switchNum = Array.IndexOf(switches, switchObject);
		//switchObject.AddInteractionPunch();
		if (switchVal[switchNum] != switchLAG[switchNum]) {return;}
		if (!switchVal[switchNum]){
			Audio.PlaySoundAtTransform("click37", transform);
			switchLables[switchNum].material = lableMATS[1];
		} else {
			Audio.PlaySoundAtTransform("click8", transform);
			switchLables[switchNum].material = lableMATS[0];
		}
		switchVal[switchNum] = !switchVal[switchNum];
	}

	void LOCKSwitch() {//KMSelectable button
		//switchObject.AddInteractionPunch();
		if (switchVal[3] != switchLAG[3]) {return;}
		if (!switchVal[3]){
			Audio.PlaySoundAtTransform("click7", transform);
			switchLights[3].material = LightColors[1];
			for (int i = 0; i < 3; i++){
				if(switchVal[i]){
					switchHOLD[i] = true;
					switchLights[i].material = LightColors[2];
				}
			}
		} else {
			Audio.PlaySoundAtTransform("click45", transform);
			switchLights[3].material = LightColors[0];
			for (int i = 0; i < 3; i++){
				switchHOLD[i] = false;
				switchLights[i].material = LightColors[0];
			}
		}
		switchVal[3] = !switchVal[3];
	}

	void Submit() {
		if(submitBOOL != submitLAG){return;}
		if (!submitBOOL){
			Audio.PlaySoundAtTransform("click36", transform);
			Audio.PlaySoundAtTransform("click8", transform);
			TPsolveLag = false;
		} else {
			Audio.PlaySoundAtTransform("click73", transform);
			switchLights[4].material = LightColors[0];
		}
		submitBOOL = !submitBOOL;
	}

	void Update() {
		if (submitBOOL != submitLAG){
			if (lagFrames != 10){
				if (lagFrames < 6){
					if (submitBOOL){submitLeverObject.Rotate(15.0f, 0.0f, 0.0f);} else {submitLeverObject.Rotate(-15.0f, 0.0f, 0.0f);}
				} else if (lagFrames == 7){
					if (submitBOOL){submitLeverObject.Rotate(10.0f, 0.0f, 0.0f);} else {submitLeverObject.Rotate(-10.0f, 0.0f, 0.0f);}
				} else if (lagFrames == 9){
					if (submitBOOL){submitLeverObject.Rotate(-10.0f, 0.0f, 0.0f);} else {submitLeverObject.Rotate(10.0f, 0.0f, 0.0f);}
				}
				lagFrames += 1;
			} else {
				lagFrames = 0;
				submitLAG = submitBOOL;
				if(submitBOOL){
					SubmitTest();
				}
			}
		}

		for (int i = 0; i < 4; i++){
			if (switchVal[i] != switchLAG[i]){
				if (switchLagFRAMES[i] != 4){
					if (switchVal[i]){switchFuseTransforms[i].Rotate(15.0f, 0.0f, 0.0f);} else {switchFuseTransforms[i].Rotate(-15.0f, 0.0f, 0.0f);}
					switchLagFRAMES[i] += 1;
				} else {
					switchLagFRAMES[i] = 0;
					switchLAG[i] = switchVal[i];
				}
			}
		}

		if (errorLAG != errorFRAME + 25){
			errorLAG += 1;
		} else if (errorFRAME < errorSET.Count){
			errorLAG = errorFRAME;
			if(!switchVal[3]){
				statusLight.material = LightColors[errorSET[errorFRAME]];
			} else {
				statusLight.material = LightColors[0];
			}
			errorFRAME += 1;
		} else {
			errorFRAME = 0;
			errorLAG = 0;
			statusLight.material = LightColors[0];
			//Debug.Log(errorFRAME + " // OFF");
		}

		if (moduleSolved) {
			if (submitBOOL && fanSpeed != 29) { fanSpeed += 1; } else if (!submitBOOL && fanSpeed != 0) { fanSpeed -= 1; }
			FanObject.Rotate(0.0f, fanSpeed, 0.0f);
			if (submitBOOL && fanLAG == 30){
				if (fanFRAME == 1){
					Audio.PlaySoundAtTransform("fan_interval_quiet", transform);
					fanFRAME += 1;
				} else if (fanFRAME < 170){
					fanFRAME += 1;
				} else {
					fanFRAME = 0;
				}
			} else if (submitBOOL && fanLAG < 30){
				fanLAG += 1;
			} else if (!submitBOOL) { fanFRAME = 0; }
		}
	}

	void SubmitTest(){
		for (int i = 0; i < 3; i++){
			if (switchHOLD[i]){debugOrder[i] = 1;} else {debugOrder[i] = 0;}
			if (switchVal[i]){debugOrder[i+3] = 1;} else {debugOrder[i+3] = 0;}
		}
		if (!moduleSolved){
			Debug.LogFormat("[Ventilation #{0}] Power switch pulled! Submitting [{1}]-[{2}]-[{3}] in the HOLD and [{4}]-[{5}]-[{6}] for the fuse switches", moduleId, debugOrder[0], debugOrder[1], debugOrder[2], debugOrder[3], debugOrder[4], debugOrder[5]);
		}
		if(!switchVal[3]){
			switchLights[4].material = LightColors[4];
			if (!moduleSolved){
				Debug.LogFormat("[Ventilation #{0}] Incomplete Circuit! Strike Recieved...", moduleId);
				GetComponent<KMBombModule>().HandleStrike();
			}
			return;
		}
		for (int i = 0; i < 3; i++){
			//Debug.Log((switchHOLD[i] != solutionVals[0, i]) + " // " + (switchVal[i] != solutionVals[1, i]));
			if(switchHOLD[i] != solutionVals[0, i] || switchVal[i] != solutionVals[1, i]){
				switchLights[4].material = LightColors[3];
				if (!moduleSolved){
					Debug.LogFormat("[Ventilation #{0}] Power Failure! Strike Recieved...", moduleId);
					GetComponent<KMBombModule>().HandleStrike();
				}
				return;
			}
		}
		switchLights[4].material = LightColors[2];
		if (!moduleSolved){
			Debug.LogFormat("[Ventilation #{0}] Power up Complete! Starting Fan...", moduleId);
			Audio.PlaySoundAtTransform("click6", transform);//click35
			Audio.PlaySoundAtTransform("click43", transform);

			Audio.PlaySoundAtTransform("misc_09", transform);
			Audio.PlaySoundAtTransform("tools_04", transform);

			Audio.PlaySoundAtTransform("winch - Marker #8", transform);
			if (!TPsolveLag) { GetComponent<KMBombModule>().HandlePass(); }
			moduleSolved = true;
		}
		return;
	}

	// Twitch Plays Support by Kilo Bites // Modified by Nimsay Ramsey

#pragma warning disable 414
	private readonly string TwitchHelpMessage = @"!{0} flip 1-4 to flip a fuse switch || !{0} submit to submit your answer.";
#pragma warning restore 414

	bool isValidPos(string n)
	{
		string[] valids = { "1", "2", "3", "4" };
		if (!valids.Contains(n))
		{
			return false;
		}
		return true;
	}

	IEnumerator ProcessTwitchCommand (string command)
	{
		yield return null;

		string[] split = command.ToUpperInvariant().Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

		if (split[0].EqualsIgnoreCase("FLIP"))
		{
			//int numberClicks = 0;
			int pos = 0;
			if (split.Length != 2)
			{
				yield return "sendtochaterror Please specify which fuse to flip!";
				yield break;
			}
			else if (!isValidPos(split[1]))
			{
				yield return "sendtochaterror " + split[1] + " is not a valid number!";
				yield break;
			}
			int.TryParse(split[1], out pos);
			//int.TryParse(split[2], out numberClicks);
			pos = pos - 1;
			if(pos == 3){lockSwitch.OnInteract();} else {switches[pos].OnInteract();}
			yield break;
		}
		
		if (split[0].EqualsIgnoreCase("SUBMIT"))
		{
			submitLever.OnInteract();
			yield return new WaitForSeconds(0.1f);
			TPsolveLag = true;
			yield return new WaitForSeconds(0.2f);
			if (submitBOOL) { yield return new WaitForSeconds(0.7f); }
			submitLever.OnInteract();
			yield return new WaitForSeconds(0.1f);
			TPsolveLag = true;
			if (submitBOOL) {
				yield return new WaitForSeconds(1.0f);
				submitLever.OnInteract();
			}
			GetComponent<KMBombModule>().HandlePass();
			yield break;
		}
	}

	IEnumerator TwitchHandleForcedSolve() //Autosolver
	{
		yield return null;

		if (submitBOOL) {
			submitLever.OnInteract();
			yield return new WaitForSeconds(0.1f);
		}

		for (int i = 0; i < 2; i++){
			if(switchVal[3] || i == 1){lockSwitch.OnInteract();}
			for(int j = 0; j < 3; j++){
				if (switchVal[j] != solutionVals[i, j]){
					switches[j].OnInteract();
					//yield return new WaitForSeconds(0.1f);
				}
				yield return new WaitForSeconds(0.1f);
			}
		}
		submitLever.OnInteract();
		yield return new WaitForSeconds(1.0f);
		submitLever.OnInteract();
	}
}
