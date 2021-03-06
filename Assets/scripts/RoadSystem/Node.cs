﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

public class Pair<T, U>
{
    public Pair()
    {
    }

    public Pair(T first, U second)
    {
        this.First = first;
        this.Second = second;
    }

    public T First { get; set; }
    public U Second { get; set; }

    public override string ToString()
    {
        return First.ToString() + " ; " + Second.ToString();
    }
};

public class ConnectionInfo{
    public Pair<float, float> margins;
    public ConnectionInfo(){
        margins = new Pair<float, float>(0f, 0f);
    }
}

public class Node : MonoBehaviour
{
    /*<MarginRight (with respect to node), MarginRight>*/
    public List<Road> connection = new List<Road>();

    List<Curve> smoothPolygonEdges = new List<Curve>();

    public Vector3 position;

    public GameObject roadCornerIndicator;
    
    RoadDrawing indicatorInst;
    
    public float arcSmoothingRadius;

    public float bezeirSmoothingScale;

    GameObject smoothInstance;

    List<Vector3> debugPoints = new List<Vector3>();

    Action<List<Curve>, Curve> addIfNotNull = (lst, c) => { if (c != null) lst.Add(c); };

    Func<Curve, List<string>, Road> createNoEntityRoadIfNotNull = (c, cfg) => 
    { if (c != null) { Road r = new Road(c, cfg, true); return r; } else return null; };

    public void Awake()
    {
        indicatorInst = GameObject.FindWithTag("Road/curveIndicator").GetComponent<RoadDrawing>();
        position.y = Mathf.NegativeInfinity;
    }

    public Vector2 twodPosition{
        get{
            return new Vector2(position.x, position.z);
        }
    }

    /*
    public Pair<float, float> getMargin(Road r){
        var conn = connection.Find((Pair<Road, ConnectionInfo> obj) => obj.First == r);
        if (!startof(r.curve))
        {
            return conn.Second.margins;
        }
        else
        {
            return new Pair<float, float>(conn.Second.margins.Second, conn.Second.margins.First);
        }
    }
    */

    public void addRoad(Road road)
    {
        Debug.Assert(position.y == Mathf.NegativeInfinity || Algebra.isclose(road.curve.at_ending(startof(road.curve)).y, position.y));

        connection.Add(road);
    }

    public void removeRoad(Road road)
    {
        if (!connection.Remove(road)){
            Debug.LogError("road to be removed is not in connection!");
        }
    }

    /*destroy redundant(generated by error) connections; regain connections deleted by error*/
    public void reEstablishConnections(List<Road> allroads){
        /*
        for (int i = connection.Count - 1; i >= 0; --i){
            if (!allroads.Contains(connection[i])){
                Destroy(connection[i].roadObject);
            }
        }
        */
        foreach(var connectedRoad in connection.ToList()){
            if (!allroads.Contains(connectedRoad)){
                Destroy(connectedRoad.roadObject);
            }
        }

        connection.Clear();
        foreach(Road r in allroads){
            if (Algebra.isclose(r.curve.at_ending(false), position) || Algebra.isclose(r.curve.at_ending(true), position))
            {
                connection.Add(r);
            }
        }

        if (connection.Count == 0)
        {
            Destroy(gameObject);
        }
        else
        {
            updateMargins();
        }
    }

    //returns one directional line for each of the road connecting to this node.
    public List<Curve> directionalLines(float length, bool reverse = false){
        /*
        List<Curve> rtn = new List<Curve>();
        foreach(Pair<Road, ConnectionInfo> connect in connection){
            Vector2 direction = connect.First.curve.direction_ending_2d(startof(connect.First.curve));
            if (reverse){
                direction = -direction;
            }
            rtn.Add(Line.TryInit(twodPosition, twodPosition + direction * Algebra.InfLength, 0f, 0f));
        }
        return rtn;
        */
        return connection.ConvertAll(delegate (Road conn)
        {
            Vector2 direction = conn.curve.direction_ending_2d(startof(conn.curve));
            if (reverse)
            {
                direction = -direction;
            }
            return Line.TryInit(twodPosition, twodPosition + direction * Algebra.InfLength, 0f, 0f);
        });
    }

    public override int GetHashCode() => position.GetHashCode();
    public override bool Equals(object obj)
    {
        Node n = obj as Node;
        return Algebra.isclose(n.position,  this.position);
    }

    /* calculate margins for all incoming road
     * plus render node*/
    public void updateMargins(){
        Destroy(smoothInstance);
        smoothPolygonEdges.Clear();

        connection = connection.OrderBy(r =>
                                        startof(r.curve) ?
                                        r.curve.angle_ending(true) : r.curve.angle_ending(false)).ToList();
                                        
        if (connection.Count > 1)
        {
            smoothInstance = Instantiate(indicatorInst.roadIndicatorPrefab, transform);
            
            for (int i = 0; i != connection.Count; ++i)
            {
                int i_hat = (i == connection.Count - 1) ? 0 : i + 1;
                //List<Curve> localSmootheners;
                var margins = smoothenCrossing(connection[i], connection[i_hat], out List<Curve> localSmootheners);
                if (smoothPolygonEdges.Count > 0 && localSmootheners.Count > 0)
                {
                    addIfNotNull(smoothPolygonEdges, Line.TryInit(smoothPolygonEdges.Last().at_ending_2d(false), localSmootheners.First().at_ending_2d(true)));
                }
                smoothPolygonEdges.AddRange(localSmootheners);

                if (position.y > 0)
                {
                    generateNodeFence(localSmootheners);
                }

                if (startof(connection[i].curve)){
                    connection[i].margin0LLength = margins.First;
                }
                else{
                    connection[i].margin1RLength = margins.First;
                }
                if (startof(connection[i_hat].curve)){
                    connection[i_hat].margin0RLength = margins.Second;
                }
                else{
                    connection[i_hat].margin1LLength = margins.Second;
                }

            }

            if (smoothPolygonEdges.Count > 0)
            {
                //Debug.Assert(smoothPolygonEdges.Count > 1);
                addIfNotNull(smoothPolygonEdges, Line.TryInit(smoothPolygonEdges.Last().at_ending_2d(false), smoothPolygonEdges.First().at_ending_2d(true)));
                RoadRenderer smoothObjConfig = smoothInstance.GetComponent<RoadRenderer>();
                smoothObjConfig.generate(new Polygon(smoothPolygonEdges), position.y, "roadsurface");
            }

        }
        updateDirectionLaneRange();
    }

    public bool startof(Curve c){
        return (c.At(0f) - position).magnitude < (c.At(1f) - position).magnitude;
    }

    Pair<float, float> smoothenCrossing(Road r1, Road r2, out List<Curve> smootheners)
    {
        float r1_angle = startof(r1.curve) ? r1.curve.angle_ending(true) : r1.curve.angle_ending(false);
        float r2_angle = startof(r2.curve) ? r2.curve.angle_ending(true) : r2.curve.angle_ending(false);
        float delta_angle = r1_angle < r2_angle ? r2_angle - r1_angle : r2_angle + 2 * Mathf.PI - r1_angle;
        this.r1 = r1;
        this.r2 = r2;
        smootheners = new List<Curve>();
        Vector2 streetCorner = approxStreetCorner();
        //debugPoints.Add(Algebra.toVector3(streetCorner));

        switch (Geometry.getAngleType(delta_angle)){
            case angleType.Sharp:
            case angleType.Blunt:
                if (c1_offset > 0f && c2_offset > 0f){
                    /*c1,c2>0*/
                    float extraSmoothingLength = arcSmoothingRadius / Mathf.Tan(delta_angle / 2);
                    addIfNotNull(smootheners, Arc.TryInit(r1.curve.at_ending_2d(startof(r1.curve), c1_offset + extraSmoothingLength) +
                                            Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), c1_offset + extraSmoothingLength) + Mathf.PI / 2) * r1.width / 2,
                                            Mathf.PI - delta_angle,
                                            r2.curve.at_ending_2d(startof(r2.curve), c2_offset + extraSmoothingLength) +
                                            Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve), c2_offset + extraSmoothingLength) - Mathf.PI / 2) * r2.width / 2));
                    return new Pair<float, float>(c1_offset + extraSmoothingLength, c2_offset + extraSmoothingLength);
                }
                if (c1_offset > 0f){
                    /*c1>0, c2<=0*/
                    float smoothRadius = -c2_offset;
                    addIfNotNull(smootheners, Arc.TryInit(r1.curve.at_ending_2d(startof(r1.curve), c1_offset + smoothRadius) +
                                            Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), c1_offset + smoothRadius) + Mathf.PI / 2) * r1.width / 2,
                                            Mathf.PI - delta_angle,
                                            r2.curve.at_ending_2d(startof(r2.curve)) +
                                            Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve)) - Mathf.PI / 2) * r2.width / 2));
                    /*TODO: calculate more precise delta_angle*/
                    return new Pair<float, float>(c1_offset + smoothRadius, 0);
                }
                if (c2_offset > 0f){
                    /*c1<0, c2>0*/
                    float smoothRadius = -c1_offset;
                    Curve smoothener = Arc.TryInit(r1.curve.at_ending_2d(startof(r1.curve)) +
                                               Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve)) + Mathf.PI / 2) * r1.width / 2,
                                               Mathf.PI - delta_angle,
                                               r2.curve.at_ending_2d(startof(r2.curve), c2_offset + smoothRadius) +
                                               Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve), c2_offset + smoothRadius) - Mathf.PI / 2) * r2.width / 2);
                    addIfNotNull(smootheners, smoothener);
                    return new Pair<float, float>(0, c2_offset + smoothRadius);
                }
                Debug.Assert(false);
                break;
            case angleType.Flat:
                if (r1.width == r2.width){
                    return new Pair<float, float>(0, 0);
                }
                float widthDiff = Math.Abs(r1.width - r2.width) / 2;
                if (r1.width > r2.width){
                    Vector2 P0 = r1.curve.at_ending_2d(startof(r1.curve)) + 
                                   Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve)) + Mathf.PI / 2) * r1.width / 2;
                    Vector2 P1 = r2.curve.at_ending_2d(startof(r2.curve), widthDiff * bezeirSmoothingScale * 0.25f) +
                                   Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), widthDiff * bezeirSmoothingScale * 0.25f) + Mathf.PI / 2) * r1.width / 2;
                    Vector2 P4 = r2.curve.at_ending_2d(startof(r2.curve) , widthDiff * bezeirSmoothingScale) +
                                   Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve), widthDiff * bezeirSmoothingScale) - Mathf.PI / 2) * r2.width / 2;
                    Vector2 P3 = r2.curve.at_ending_2d(startof(r2.curve), widthDiff * bezeirSmoothingScale * 0.75f) +
                                   Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve), widthDiff * bezeirSmoothingScale * 0.75f) - Mathf.PI / 2) * r2.width / 2;
                    Vector2 P2 = (P1 + P3) / 2;
                    addIfNotNull(smootheners, Bezeir.TryInit(P0, P1, P2));
                    addIfNotNull(smootheners, Bezeir.TryInit(P2, P3, P4));
                    return new Pair<float, float>(0f, widthDiff * bezeirSmoothingScale);
                }
                else{
                    Vector2 P0 = r1.curve.at_ending_2d(startof(r1.curve), widthDiff * bezeirSmoothingScale)
                                   + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), widthDiff * bezeirSmoothingScale) + Mathf.PI / 2) * r1.width / 2;
                    Vector2 P1 = r1.curve.at_ending_2d(startof(r1.curve), widthDiff * bezeirSmoothingScale * 0.75f)
                                   + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), widthDiff * bezeirSmoothingScale * 0.75f) + Mathf.PI / 2) * r1.width / 2;
                    Vector2 P3 = r1.curve.at_ending_2d(startof(r1.curve), widthDiff * bezeirSmoothingScale * 0.25f)
                                   + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), widthDiff * bezeirSmoothingScale * 0.25f) + Mathf.PI / 2) * r2.width / 2;
                    Vector2 P4 = r2.curve.at_ending_2d(startof(r2.curve)) +
                                   Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve)) - Mathf.PI / 2) * r2.width / 2;
                    Vector2 P2 = (P1 + P3) / 2;
                    addIfNotNull(smootheners, Bezeir.TryInit(P0, P1, P2));
                    addIfNotNull(smootheners, Bezeir.TryInit(P2, P3, P4));
                    return new Pair<float, float>(widthDiff * bezeirSmoothingScale, 0f);
                }
            case angleType.Reflex:
                float arcRadius = Mathf.Max(r1.width / 2, r2.width / 2);
                float bWidthDiff = Mathf.Abs(r1.width - r2.width) / 2;
                Curve arcSmoothener = Arc.TryInit(twodPosition,
                                        twodPosition + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve)) + Mathf.PI / 2) * arcRadius,
                                        delta_angle - Mathf.PI);
                if (r1.width == r2.width){
                    addIfNotNull(smootheners, arcSmoothener);
                    return new Pair<float, float>(0f, 0f);
                }
                if (r1.width > r2.width){
                    Vector2 P0 = r2.curve.at_ending_2d(startof(r2.curve)) + 
                                   Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve)) - Mathf.PI / 2) * r1.width / 2;
                    Vector2 P1 = r2.curve.at_ending_2d(startof(r2.curve), bWidthDiff * bezeirSmoothingScale * 0.25f) +
                                   Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve), bWidthDiff * bezeirSmoothingScale * 0.25f) - Mathf.PI / 2) * r1.width / 2;
                    Vector2 P4 = r2.curve.at_ending_2d(startof(r2.curve), bWidthDiff * bezeirSmoothingScale) 
                                   + Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve), bWidthDiff * bezeirSmoothingScale) - Mathf.PI / 2) * r2.width / 2;
                    Vector2 P3 = r2.curve.at_ending_2d(startof(r2.curve), bWidthDiff * bezeirSmoothingScale * 0.75f)
                                   + Algebra.angle2dir(r2.curve.angle_ending(startof(r2.curve), bWidthDiff * bezeirSmoothingScale * 0.75f) - Mathf.PI / 2) * r2.width / 2;
                    Vector2 P2 = (P1 + P3) / 2;
                    addIfNotNull(smootheners, arcSmoothener);
                    addIfNotNull(smootheners, Bezeir.TryInit(P0, P1, P2));
                    addIfNotNull(smootheners, Bezeir.TryInit(P2, P3, P4));
                    return new Pair<float, float>(0f, bWidthDiff * bezeirSmoothingScale);
                }
                else{
                    Vector2 P0 = r1.curve.at_ending_2d(startof(r1.curve), bWidthDiff * bezeirSmoothingScale)
                                   + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), bWidthDiff * bezeirSmoothingScale) + Mathf.PI / 2) * r1.width / 2;
                    Vector2 P1 = r1.curve.at_ending_2d(startof(r1.curve), bWidthDiff * bezeirSmoothingScale * 0.75f)
                                   + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), bWidthDiff * bezeirSmoothingScale * 0.75f) + Mathf.PI / 2) * r1.width / 2;
                    Vector2 P3 = r1.curve.at_ending_2d(startof(r1.curve), bWidthDiff * bezeirSmoothingScale * 0.25f)
                                   + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve), bWidthDiff * bezeirSmoothingScale * 0.25f) + Mathf.PI / 2) * r2.width / 2;
                    Vector2 P4 = r1.curve.at_ending_2d(startof(r1.curve))
                                   + Algebra.angle2dir(r1.curve.angle_ending(startof(r1.curve)) + Mathf.PI / 2) * r2.width / 2;
                    Vector2 P2 = (P1 + P3) / 2;
                    addIfNotNull(smootheners, Bezeir.TryInit(P0, P1, P2));
                    addIfNotNull(smootheners, Bezeir.TryInit(P2, P3, P4));
                    addIfNotNull(smootheners, arcSmoothener);
                    return new Pair<float, float>(bWidthDiff * bezeirSmoothingScale, 0f);
                }

            default:
                return new Pair<float, float>(-1f, -1f);
        }
        return new Pair<float, float>(-1f, -1f);

    }

    internal List<Vector2> getNeighborDirections(Vector2 direction)
    {
        if (connection.Count <= 2){
            return connection.ConvertAll((input) => input.curve.direction_ending_2d(startof(input.curve)));
        }

        List<float> anglesFromDirection =
            connection.ConvertAll((input) => Algebra.signedAngleToPositive(Vector2.SignedAngle(direction, input.curve.direction_ending_2d(startof(input.curve)))));

        return connection.FindAll((Road arg1) =>
                                  Algebra.signedAngleToPositive(Vector2.SignedAngle(direction, arg1.curve.direction_ending_2d(startof(arg1.curve)))) 
                                  == anglesFromDirection.Max() 
                                 ||
                                  Algebra.signedAngleToPositive(Vector2.SignedAngle(direction, arg1.curve.direction_ending_2d(startof(arg1.curve))))
                                  == anglesFromDirection.Min()).
                         ConvertAll((input) => input.curve.direction_ending_2d(startof(input.curve)));
    }

    /*
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(new Vector3(streetcorner.x, 2f, streetcorner.y), 0.1f);
    }
    */

    Road r1, r2;
    float c1_offset, c2_offset;

    float C1sidepointToC2Sidepoint(float l1)
    {
        float c2_tan_angle = r2.curve.angle_ending(startof(r2.curve), offset: c2_offset);
        Vector2 c2_normDir = new Vector2(Mathf.Cos(c2_tan_angle - Mathf.PI / 2), Mathf.Sin(c2_tan_angle - Mathf.PI / 2));
        float c1_tan_angle = r1.curve.angle_ending(startof(r1.curve), offset: l1);
        Vector2 c1_normDir = new Vector2(Mathf.Cos(c1_tan_angle + Mathf.PI / 2), Mathf.Sin(c1_tan_angle + Mathf.PI / 2));
        return ((r1.curve.at_ending_2d(startof(r1.curve), l1) + c1_normDir * r1.width / 2f) -
                (r2.curve.at_ending_2d(startof(r2.curve), c2_offset) + c2_normDir * r2.width / 2f)).magnitude;
    }

    float C2sidepointToC1Sidepoint(float l2)
    {
        float c2_tan_angle = r2.curve.angle_ending(startof(r2.curve), offset: l2);
        Vector2 c2_normDir = new Vector2(Mathf.Cos(c2_tan_angle - Mathf.PI / 2), Mathf.Sin(c2_tan_angle - Mathf.PI / 2));
        float c1_tan_angle = r1.curve.angle_ending(startof(r1.curve), offset: c1_offset);
        Vector2 c1_normDir = new Vector2(Mathf.Cos(c1_tan_angle + Mathf.PI / 2), Mathf.Sin(c1_tan_angle + Mathf.PI / 2));
        return ((r1.curve.at_ending_2d(startof(r1.curve), c1_offset) + c1_normDir * r1.width / 2f) -
                (r2.curve.at_ending_2d(startof(r2.curve), l2) + c2_normDir * r2.width / 2f)).magnitude;
    }

    Vector2 approxStreetCorner(){
        c1_offset = 0f;
        c2_offset = 0f;
        int loopCount = 0;

        while (loopCount < 20){
            float c1_new_offset = Algebra.minArg(C1sidepointToC2Sidepoint, c1_offset);
            float c1_diff = Mathf.Abs(c1_offset - c1_new_offset);
            c1_offset = c1_new_offset;

            float c2_new_offset = Algebra.minArg(C2sidepointToC1Sidepoint, c2_offset);
            float c2_diff = Mathf.Abs(c2_offset - c2_new_offset);
            c2_offset = c2_new_offset;
            if (c1_diff + c2_diff < 1e-3)
                break;
        }
        if (loopCount == 20){
            throw new Exception();
        }

        float c2_tan_angle = r2.curve.angle_ending(startof(r2.curve), offset: c2_offset);
        Vector2 c2_normDir = new Vector2(Mathf.Cos(c2_tan_angle - Mathf.PI / 2), Mathf.Sin(c2_tan_angle - Mathf.PI / 2));

        return r2.curve.at_ending_2d(startof(r2.curve), c2_offset) + c2_normDir * r2.width / 2f;

    }

    void generateNodeFence(List<Curve> smoothener){
        foreach (Curve nodeSide in smoothener)
        {
            nodeSide.z_start = position.y;
            RoadRenderer smoothObjConfig = smoothInstance.GetComponent<RoadRenderer>();
            smoothObjConfig.generate(nodeSide, new List<String> { "singlefence" });
            nodeSide.z_start = 0f;
        }
    }

    /*End of modelling part; 
     * Begin of traffic part*/

    /*Range of valid lanes of inRoad going to outRoad*/
    public Pair<int, int> getValidInRoadLanes(Road inRoad, Road outRoad)
    {
        int i1 = connection.IndexOf(inRoad);
        int i2 = connection.IndexOf(outRoad);
        return outLaneRange[i1, i2];
    }

    /*Range of valid lanes of outRoad accepting traffic from inRoad*/
    public Pair<int, int> getValidOutRoadLanes(Road inRoad, Road outRoad)
    {
        int i1 = connection.IndexOf(inRoad);
        int i2 = connection.IndexOf(outRoad);
        return inLaneRange[i2, i1];
    }

    public Road getVirtualRoad(Road r1, Road r2)
    {
        int i1 = connection.IndexOf(r1);
        int i2 = connection.IndexOf(r2);
        return virtualRoads[i1, i2];
    }

    public List<Road> AllVirtualRoads{
        get
        {
            List<Road> vt = new List<Road>();
            for (int i = 0; i != connection.Count; ++i){
                for (int j = 0; j != connection.Count; ++j){
                    if (virtualRoads[i,j] != null){
                        vt.Add(virtualRoads[i, j]);
                    }
                }
            }
            return vt;
        }
    }

    Road generateVirtualRoad(int i1, int i2){
        Road r1 = connection[i1];
        Road r2 = connection[i2];

        if (outLaneRange[i1, i2] == null){
            return null;
        }
        int loOutLaneNum = outLaneRange[i1, i2].First;
        int hiOutLaneNum = outLaneRange[i1, i2].Second;

        int loInLaneNum = inLaneRange[i2, i1].First;
        int hiInLaneNum = inLaneRange[i2, i1].Second;

        Debug.Assert(hiOutLaneNum - loOutLaneNum == hiInLaneNum - loInLaneNum);

        float r1_margin = startof(r1.curve) ? r1.margin0Param : r1.margin1Param;
        float r2_margin = startof(r2.curve) ? r2.margin0Param : r2.margin1Param;

        float r1_radiOffset = 0.5f * (r1.getLaneCenterOffset(loOutLaneNum, !startof(r1.curve)) + r1.getLaneCenterOffset(hiOutLaneNum, !startof(r1.curve)));
        float r2_radiOffset = 0.5f * (r2.getLaneCenterOffset(loInLaneNum, startof(r2.curve)) + r2.getLaneCenterOffset(hiInLaneNum, startof(r2.curve)));
        Vector3 r1_endPos = r1.at(r1_margin) + r1.rightNormal(r1_margin) * r1_radiOffset;
        Vector3 r2_endPos = r2.at(r2_margin) + r2.rightNormal(r2_margin) * r2_radiOffset;

        List<string> virtualRoadLaneCfg = new List<string>();
        int virtualRoadLaneCount = hiOutLaneNum - loOutLaneNum + 1;
        for (int i = 0; i != virtualRoadLaneCount; ++i){
            virtualRoadLaneCfg.Add("lane");
            if (i != virtualRoadLaneCount - 1){
                virtualRoadLaneCfg.Add("dash_white");
            }
        }
        
        Vector2 r1_direction = startof(r1.curve) ? -r1.curve.direction_2d(r1_margin) : r1.curve.direction_2d(r1_margin);
        Vector2 r2_direction = startof(r2.curve) ? -r2.curve.direction_2d(r2_margin) : r2.curve.direction_2d(r2_margin);

        if (Geometry.Parallel(r1_direction, r2_direction))
        {
            /*TODO: perform a U turn when r1 = r2*/
            if (Algebra.isRoadNodeClose(r1_endPos, r2_endPos)){
                /*exact same lane config for neighbors, just go straight*/
                return null;
            }
            //return new Road(Line.TryInit(r1_endPos, r2_endPos), virtualRoadLaneCfg, _noEntity: true);
            return createNoEntityRoadIfNotNull(Line.TryInit(r1_endPos, r2_endPos), virtualRoadLaneCfg);
        }
        else
        {
            Curve l1 = Line.TryInit(Algebra.toVector2(r1_endPos), Algebra.toVector2(r1_endPos) + Algebra.InfLength * r1_direction, r1_endPos.y, r1_endPos.y);
            Curve l2 = Line.TryInit(Algebra.toVector2(r2_endPos), Algebra.toVector2(r2_endPos) + Algebra.InfLength * r2_direction, r2_endPos.y, r2_endPos.y);
            List<Vector3> intereSectionPoint = Geometry.curveIntersect(l1, l2);
            if (intereSectionPoint.Count == 1)
            {
                //return new Road(Bezeir.TryInit(r1_endPos, intereSectionPoint.First(), r2_endPos), virtualRoadLaneCfg, _noEntity: true);
                return createNoEntityRoadIfNotNull(Bezeir.TryInit(r1_endPos, intereSectionPoint.First(), r2_endPos), virtualRoadLaneCfg);
            }
            else{
                //return new Road(Line.TryInit(r1_endPos, r2_endPos), virtualRoadLaneCfg, _noEntity: true);
                return createNoEntityRoadIfNotNull(Line.TryInit(r1_endPos, r2_endPos), virtualRoadLaneCfg);
            }
        }
    }

    Pair<int, int>[,] outLaneRange = null;
    Pair<int, int>[,] inLaneRange = null;
    Road[,] virtualRoads = null;

    void updateDirectionLaneRange(){
        int dirCount = connection.Count;

        outLaneRange = new Pair<int, int>[dirCount, dirCount];
        for (int i = 0; i != dirCount; ++i){
            int incomingLanesNum = connection[i].directionalLaneCount(!startof(connection[i].curve));
            int outgoingLaneNum = connection.Sum(r => r.directionalLaneCount(startof(r.curve)));
            //Debug.Log("for road # " + connection[i].First.curve + " ,incoming= " + incomingLanesNum + " outgoing= " + outgoingLaneNum);

            int beingAssigned = 0;

            if (incomingLanesNum < outgoingLaneNum)
            {
                //supply smaller than demand
                for (int j = i + 1; j <= i + dirCount; ++j)
                {
                    int target = j % dirCount;
                    int targetLaneNum = connection[target].directionalLaneCount(startof(connection[target].curve));

                    if (incomingLanesNum > 0 && targetLaneNum > 0)
                    {
                        int lo = incomingLanesNum * (beingAssigned + 1) / (outgoingLaneNum + 1);
                        int hi = incomingLanesNum * (beingAssigned + targetLaneNum) / (outgoingLaneNum + 1);

                        outLaneRange[i, target] = new Pair<int, int>(lo, hi);
                        beingAssigned += targetLaneNum;
                    }
                }
            }
            else
            {
                //supply satisfies demand
                for (int j = i + 1; j <= i + dirCount; ++j){
                    int target = j % dirCount;
                    int targetLaneNum = connection[target].directionalLaneCount(startof(connection[target].curve));
                    if (incomingLanesNum > 0 && targetLaneNum > 0){
                        outLaneRange[i, target] = new Pair<int, int>(beingAssigned, beingAssigned + targetLaneNum - 1);
                        beingAssigned += targetLaneNum;
                    }
                }
            }
        }

        inLaneRange = new Pair<int, int>[dirCount, dirCount];
        for (int i = 0; i != dirCount; ++i){
            int myCapacity = connection[i].directionalLaneCount(startof(connection[i].curve));

            for (int j = i + 1; j <= i + dirCount; ++j){
                int target = j % dirCount;

                if (outLaneRange[target, i] != null)
                {
                    int incomingCount = outLaneRange[target, i].Second - outLaneRange[target, i].First + 1;
                    Debug.Assert(incomingCount <= myCapacity);
                    inLaneRange[i, target] = (j == i + dirCount - 1) ? new Pair<int, int>(0, incomingCount - 1) :
                    new Pair<int, int>(myCapacity - incomingCount, myCapacity - 1);
                }
            }
        }

        virtualRoads = new Road[dirCount, dirCount];
        for (int i = 0; i != dirCount; ++i){
            for (int j = 0; j != dirCount; ++j){
                virtualRoads[i, j] = generateVirtualRoad(i, j);
            }
        }
    }

    public override string ToString()
    {
        return position.ToString();
    }
}
