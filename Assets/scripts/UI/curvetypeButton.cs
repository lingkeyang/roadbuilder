﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class curvetypeButton : MonoBehaviour
{
    
    RoadDrawing indicator;

    public void Awake()
    {
        GameObject indicatorInst = GameObject.FindWithTag("Road/curveIndicator");
        indicator = indicatorInst.GetComponent<RoadDrawing>();
    }

    public void Update()
    {
        if (indicator.indicatorType != IndicatorType.none && !EventSystem.current.IsPointerOverGameObject()){
            Vector2 twodhitpoint = GameObject.FindWithTag("MainCamera").GetComponent<RayHit>().hitpoint;
            if (Input.GetMouseButtonDown(0)){
                indicator.fixControlPoint(twodhitpoint);
            }
            else{
                indicator.setControlPoint(twodhitpoint);
            }
        }
    }

    public void setToLineMode()
    {
        if (indicator.indicatorType == IndicatorType.line)
        {
            indicator.reset();
        }
        else
        {
            indicator.indicatorType = IndicatorType.line;
        }
    }

    public void setToArcMode(){
        if (indicator.indicatorType == IndicatorType.arc)
        {
            indicator.reset();
        }
        else{
            indicator.indicatorType = IndicatorType.arc;
        }
    }
    public void setToBezierMode(){
        if (indicator.indicatorType == IndicatorType.bezeir){
            indicator.reset();
        }
        else{
            indicator.indicatorType = IndicatorType.bezeir;
        }
    }

    public void setToDeleteMode(){
        indicator.indicatorType = IndicatorType.delete;
    }

}
