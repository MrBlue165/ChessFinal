using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SpecialMove
{
    None = 0,
    EnPassant,
    Castle,
    Promotion
}

public enum DrawType
{
    None = 0,
    Stalemate,
    InsufficientMaterial,
    FiftyMoveRule
}

public class Chessboard : MonoBehaviour
{
    [Header("Art")] 
    [SerializeField] private Material tileMaterial;
    [SerializeField] private float tileSize = 1.0f;
    [SerializeField] private float yOffset = 1.0f;
    [SerializeField] private Vector3 boardCenter = Vector3.zero;
    [SerializeField] private float deathSize = 0.3f;
    [SerializeField] private float deathSpacing = 0.3f;
    [SerializeField] private float dragOffset = 1.0f;
    [SerializeField] private float deathOffset = 1.0f;
    [SerializeField] private GameObject victoryScreen;
    
    [Header("Draw UI")]
    [SerializeField] private GameObject drawScreen;           // ← assign in Inspector

    [Header("Prefabs & Materials")] 
    [SerializeField] private GameObject[] prefabs;
    [SerializeField] private Material[] teamMaterials;
    
    
    [Header("Camera Rotation (for playing both sides)")]
    [SerializeField] private Transform cameraTransform;           // ← drag your Main Camera here in Inspector
    [SerializeField] private float cameraRotationDuration = 0.9f; // how long the flip takes (seconds)
    [SerializeField] private AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private bool isCameraRotating = false;
    private Quaternion cameraStartRot;
    private Quaternion cameraTargetRot;
    private float cameraRotTimer = 0f;
    private Vector3 cameraStartPos;
    private Vector3 cameraTargetPos;

    // LOGIC
    private ChessPiece[,] chessPieces;
    private ChessPiece currentPiece;
    private List<Vector2Int> availableMoves = new List<Vector2Int>();
    private List<ChessPiece> deadWhites = new List<ChessPiece>();
    private List<ChessPiece> deadBlacks = new List<ChessPiece>();
    private const int TILE_COUNT_X = 8;
    private const int TILE_COUNT_Y = 8;
    private GameObject[,] tiles;
    private Camera currentCamera;
    private Vector2Int currentHover;
    private Vector3 bounds;
    private bool isWhiteTurn;
    private SpecialMove specialMove;
    private List<Vector2Int[]> moveList = new List<Vector2Int[]>();
    // Add these fields to your Chessboard class
    private bool isInCheck = false;
    private Vector2Int? kingInCheckPosition = null;
    private Vector2Int? checkingPiecePosition = null;
    
    // Draw rules fields
    private int fiftyMoveCounter = 0;

    private void Awake()
    {
        isWhiteTurn = true;
        GenerateAllTiles(tileSize, TILE_COUNT_X, TILE_COUNT_Y);
        GenerateAllPieces();
        PositionAllPieces();
    }

    public void Update()
    {
        if (isCameraRotating)
        {
            cameraRotTimer += Time.deltaTime / cameraRotationDuration;
            float t = rotationCurve.Evaluate(cameraRotTimer);
    
            cameraTransform.rotation = Quaternion.Lerp(cameraStartRot, cameraTargetRot, t);
            cameraTransform.position = Vector3.Lerp(cameraStartPos, cameraTargetPos, t);

            if (cameraRotTimer >= 1f)
            {
                cameraTransform.rotation = cameraTargetRot;
                cameraTransform.position = cameraTargetPos;
                isCameraRotating = false;
            }
        }

        
        
        if (!currentCamera)
        {
            currentCamera = Camera.main;
            return;
        }

        RaycastHit info;
        Ray ray = currentCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out info, 100, LayerMask.GetMask("Tile", "Hover", "Highlight", "CheckKing", "CheckPiece")))
        {
            Vector2Int hitPosition = LookUpTileIndex(info.transform.gameObject);

            if (currentHover == -Vector2Int.one)
            {
                currentHover = hitPosition;
                tiles[hitPosition.x, hitPosition.y].layer = LayerMask.NameToLayer("Hover");
            }

            if (currentHover != hitPosition)
            {
                tiles[currentHover.x, currentHover.y].layer = (ContainsValidMove(ref availableMoves, currentHover)) ? LayerMask.NameToLayer("Highlight") : LayerMask.NameToLayer("Tile");
                RestoreTileLayer(currentHover);
                currentHover = hitPosition;
                tiles[hitPosition.x, hitPosition.y].layer = LayerMask.NameToLayer("Hover");
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (chessPieces[hitPosition.x, hitPosition.y] != null)
                {
                    if ((chessPieces[hitPosition.x, hitPosition.y].team == 0 && isWhiteTurn) || (chessPieces[hitPosition.x, hitPosition.y].team == 1 && !isWhiteTurn))
                    {
                        currentPiece = chessPieces[hitPosition.x, hitPosition.y];
                        
                        
                        availableMoves = currentPiece.GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
                        specialMove = currentPiece.GetSpecialMoves(ref chessPieces, ref moveList, ref availableMoves);
                        
                        PreventCheck();
                        HighlightTiles();
                    }
                }
            }

            if (currentPiece != null && Input.GetMouseButtonUp(0))
            {
                Vector2Int previousPosition = new Vector2Int(currentPiece.currentX, currentPiece.currentY);
                bool validMove = MoveTo(currentPiece, hitPosition.x, hitPosition.y);
                if (!validMove)
                {
                    currentPiece.SetPosition(GetTileCenter(previousPosition.x, previousPosition.y));
                    currentPiece = null;
                }
                currentPiece = null;
                
                RemoveHighlightTiles();
            }

        }
        else {
            if (currentHover != -Vector2Int.one)
            {
                int previousLayer = GetAppropriateLayerForTile(currentHover);
                
                RestoreTileLayer(currentHover);
                tiles[currentHover.x, currentHover.y].layer = previousLayer;
                currentHover = -Vector2Int.one;
            }

            if (currentPiece && Input.GetMouseButtonUp(0))
            {
                currentPiece.SetPosition(GetTileCenter(currentPiece.currentX, currentPiece.currentY));
                currentPiece = null;
            }
        
        }

        if (currentPiece)
        {
            Plane horizontalPlane = new Plane(Vector3.up, Vector3.up * yOffset);
            float distance = 0.0f;
            if (horizontalPlane.Raycast(ray, out distance))
                currentPiece.SetPosition(ray.GetPoint(distance)+ Vector3.up * dragOffset);
        }
    }
    private void RestoreTileLayer(Vector2Int position)
    {
        // Priority order: CheckKing > CheckPiece > Highlight > Tile
    
        if (kingInCheckPosition.HasValue && position == kingInCheckPosition.Value)
        {
            tiles[position.x, position.y].layer = LayerMask.NameToLayer("CheckKing");
        }
        else if (checkingPiecePosition.HasValue && position == checkingPiecePosition.Value)
        {
            tiles[position.x, position.y].layer = LayerMask.NameToLayer("CheckPiece");
        }
        else if (ContainsValidMove(ref availableMoves, position))
        {
            tiles[position.x, position.y].layer = LayerMask.NameToLayer("Highlight");
        }
        else
        {
            tiles[position.x, position.y].layer = LayerMask.NameToLayer("Tile");
        }
    }

    
    private int GetAppropriateLayerForTile(Vector2Int position)
    {
        // Priority: CheckKing > CheckPiece > Highlight > Tile
    
        if (kingInCheckPosition.HasValue && 
            kingInCheckPosition.Value.x == position.x && 
            kingInCheckPosition.Value.y == position.y)
        {
            return LayerMask.NameToLayer("CheckKing");
        }
    
        if (checkingPiecePosition.HasValue && 
            checkingPiecePosition.Value.x == position.x && 
            checkingPiecePosition.Value.y == position.y)
        {
            return LayerMask.NameToLayer("CheckPiece");
        }
    
        if (ContainsValidMove(ref availableMoves, position))
        {
            return LayerMask.NameToLayer("Highlight");
        }
    
        return LayerMask.NameToLayer("Tile");
    }
    
    private void UpdateCheckIndicators()
{
    // Reset previous check indicators
    ClearCheckIndicators();
    
    // Determine if current player's king is in check
    DetermineCheckState();
    
    // Apply new check indicators if needed
    if (isInCheck && kingInCheckPosition.HasValue)
    {
        // Highlight king's tile with CheckKing layer
        tiles[kingInCheckPosition.Value.x, kingInCheckPosition.Value.y].layer = LayerMask.NameToLayer("CheckKing");
        
        // Highlight checking piece with CheckPiece layer
        if (checkingPiecePosition.HasValue)
        {
            tiles[checkingPiecePosition.Value.x, checkingPiecePosition.Value.y].layer = LayerMask.NameToLayer("CheckPiece");
        }
    }
}
    
    private bool IsTileInCheck(Vector2Int position)
    {
        return (kingInCheckPosition.HasValue && position == kingInCheckPosition.Value) ||
               (checkingPiecePosition.HasValue && position == checkingPiecePosition.Value);
    }

    private void ClearCheckIndicators()
    {
        // Only clear if we have positions to clear
        if (kingInCheckPosition.HasValue)
        {
            // Only reset if it's not currently highlighted for moves
            if (!ContainsValidMove(ref availableMoves, kingInCheckPosition.Value))
            {
                tiles[kingInCheckPosition.Value.x, kingInCheckPosition.Value.y].layer = LayerMask.NameToLayer("Tile");
            }
        }
    
        if (checkingPiecePosition.HasValue)
        {
            if (!ContainsValidMove(ref availableMoves, checkingPiecePosition.Value))
            {
                tiles[checkingPiecePosition.Value.x, checkingPiecePosition.Value.y].layer = LayerMask.NameToLayer("Tile");
            }
        }
    
        isInCheck = false;
        kingInCheckPosition = null;
        checkingPiecePosition = null;
    }
    private void DetermineCheckState()
    {
        int currentTeam = isWhiteTurn ? 0 : 1;
        ChessPiece currentKing = null;
        List<ChessPiece> enemyPieces = new List<ChessPiece>();
    
        // Find current player's king and enemy pieces
        for (int x = 0; x < TILE_COUNT_X; x++)
        {
            for (int y = 0; y < TILE_COUNT_Y; y++)
            {
                if (chessPieces[x, y] != null)
                {
                    if (chessPieces[x, y].type == ChessPieceType.King && chessPieces[x, y].team == currentTeam)
                    {
                        currentKing = chessPieces[x, y];
                    }
                    else if (chessPieces[x, y].team != currentTeam)
                    {
                        enemyPieces.Add(chessPieces[x, y]);
                    }
                }
            }
        }
    
        if (currentKing == null) return;
    
        // Check if any enemy piece can capture the king
        Vector2Int kingPos = new Vector2Int(currentKing.currentX, currentKing.currentY);
    
        foreach (var enemy in enemyPieces)
        {
            List<Vector2Int> enemyMoves = enemy.GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
        
            if (ContainsValidMove(ref enemyMoves, kingPos))
            {
                isInCheck = true;
                kingInCheckPosition = kingPos;
                checkingPiecePosition = new Vector2Int(enemy.currentX, enemy.currentY);
            
                // Update the tile layers, but preserve hover if needed
                if (currentHover != kingPos)
                {
                    tiles[kingPos.x, kingPos.y].layer = LayerMask.NameToLayer("CheckKing");
                }
            
                if (currentHover != checkingPiecePosition.Value)
                {
                    tiles[checkingPiecePosition.Value.x, checkingPiecePosition.Value.y].layer = LayerMask.NameToLayer("CheckPiece");
                }
            
                return; // Found at least one checking piece
            }
        }
    }
    private void ClearAllHighlights()
    {
        // Clear all highlight tiles, but preserve check indicators
        for (int x = 0; x < TILE_COUNT_X; x++)
        {
            for (int y = 0; y < TILE_COUNT_Y; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
            
                // Only reset if it's not a check indicator and not the current hover
                if (!IsTileInCheck(pos) && pos != currentHover)
                {
                    tiles[x, y].layer = LayerMask.NameToLayer("Tile");
                }
                // If it's a check indicator but not hover, restore check layer
                else if (IsTileInCheck(pos) && pos != currentHover)
                {
                    if (kingInCheckPosition.HasValue && pos == kingInCheckPosition.Value)
                        tiles[x, y].layer = LayerMask.NameToLayer("CheckKing");
                    else if (checkingPiecePosition.HasValue && pos == checkingPiecePosition.Value)
                        tiles[x, y].layer = LayerMask.NameToLayer("CheckPiece");
                }
            }
        }
    }
    
    // Generate Board
    private void GenerateAllTiles(float tileSize, int tileCountX, int tileCountY)
    {
        yOffset += transform.position.y;
        bounds = new Vector3((tileCountX / 2) * tileSize, 0, (tileCountX / 2) * tileSize) + boardCenter;
        
        
        tiles = new GameObject[tileCountX, tileCountY];
        for (int x = 0; x < tileCountX; x++)
        for (int y = 0; y < tileCountY; y++)
            tiles[x, y] = GenerateSingleTile(tileSize, x, y);
    }

    private GameObject GenerateSingleTile(float tileSize, int x, int y)
    {
        GameObject tileObject = new GameObject(string.Format("X:{0} Y:{1}", x, y));
        tileObject.transform.parent = transform;
        
        Mesh mesh = new Mesh();
        tileObject.AddComponent<MeshFilter>().mesh = mesh;
        tileObject.AddComponent<MeshRenderer>().material = tileMaterial;
        
        
        Vector3[] vertices = new Vector3[4];
        vertices[0] = new Vector3(x* tileSize, yOffset, y* tileSize) - bounds;
        vertices[1] = new Vector3(x* tileSize, yOffset, (y+1)* tileSize) - bounds;
        vertices[2] = new Vector3((x+1)* tileSize, yOffset, y* tileSize) - bounds;
        vertices[3] = new Vector3((x+1)* tileSize, yOffset, (y+1)* tileSize) - bounds;
        
        
        int[] triangles = new int[] { 0, 1, 2, 1, 3, 2 };
        
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        
        mesh.RecalculateNormals();
        
        
        tileObject.layer = LayerMask.NameToLayer("Tile");
        tileObject.AddComponent<BoxCollider>();
        
        return tileObject;
    }
    
    //Generate Pieces
    private void GenerateAllPieces()
    {
        chessPieces = new ChessPiece[TILE_COUNT_X, TILE_COUNT_Y];
        
        int whiteTeam =0, blackTeam = 1;
        
        chessPieces[0,0] = SpawnSinglePiece(ChessPieceType.Rook, whiteTeam);
        chessPieces[1,0] = SpawnSinglePiece(ChessPieceType.Knight, whiteTeam);
        chessPieces[2,0] = SpawnSinglePiece(ChessPieceType.Bishop, whiteTeam);
        chessPieces[3,0] = SpawnSinglePiece(ChessPieceType.Queen, whiteTeam);
        chessPieces[4,0] = SpawnSinglePiece(ChessPieceType.King, whiteTeam);
        chessPieces[5,0] = SpawnSinglePiece(ChessPieceType.Bishop, whiteTeam);
        chessPieces[6,0] = SpawnSinglePiece(ChessPieceType.Knight, whiteTeam);
        chessPieces[7,0] = SpawnSinglePiece(ChessPieceType.Rook, whiteTeam);
        
        for(int i = 0; i < TILE_COUNT_X; i++)
            chessPieces[i,1] = SpawnSinglePiece(ChessPieceType.Pawn, whiteTeam);
        
        
        chessPieces[0,7] = SpawnSinglePiece(ChessPieceType.Rook, blackTeam);
        chessPieces[1,7] = SpawnSinglePiece(ChessPieceType.Knight, blackTeam);
        chessPieces[2,7] = SpawnSinglePiece(ChessPieceType.Bishop, blackTeam);
        chessPieces[3,7] = SpawnSinglePiece(ChessPieceType.Queen, blackTeam);
        chessPieces[4,7] = SpawnSinglePiece(ChessPieceType.King, blackTeam);
        chessPieces[5,7] = SpawnSinglePiece(ChessPieceType.Bishop, blackTeam);
        chessPieces[6,7] = SpawnSinglePiece(ChessPieceType.Knight, blackTeam);
        chessPieces[7,7] = SpawnSinglePiece(ChessPieceType.Rook, blackTeam);
        
        for(int i = 0; i < TILE_COUNT_X; i++)
            chessPieces[i,6] = SpawnSinglePiece(ChessPieceType.Pawn, blackTeam);
        
    }

    private ChessPiece SpawnSinglePiece(ChessPieceType type, int team)
    {
        ChessPiece cp = Instantiate(prefabs[(int)type -1], transform).GetComponent<ChessPiece>();
        
        cp.type = type;
        cp.team = team;
        cp.GetComponent<MeshRenderer>().material = teamMaterials[team];
        
        return cp;
    }
    
    //Positioning
    private void PositionAllPieces()
    {
        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (chessPieces[x, y] != null)
                    PositionSinglePiece(x, y, true);
    }

    private void PositionSinglePiece(int x, int y, bool force = false)
    {
        chessPieces[x, y].currentX = x;
        chessPieces[x, y].currentY = y;
        chessPieces[x, y].SetPosition(GetTileCenter(x, y), force);
    }

    private Vector3 GetTileCenter(int x, int y)
    {
        return new Vector3(x * tileSize,yOffset , y * tileSize) - bounds + new Vector3(tileSize / 2, 0, tileSize / 2);
    }

    private void HighlightTiles()
    {
        for (int i = 0; i < availableMoves.Count; i++)
            tiles[availableMoves[i].x, availableMoves[i].y].layer = LayerMask.NameToLayer("Highlight");
    }    
    private void RemoveHighlightTiles()
    {
        // Instead of just removing from availableMoves list, clear all highlights
        for (int i = 0; i < availableMoves.Count; i++)
        {
            Vector2Int pos = availableMoves[i];
            // Only reset if it's not a check indicator and not the current hover
            if (!IsTileInCheck(pos) && pos != currentHover)
            {
                tiles[pos.x, pos.y].layer = LayerMask.NameToLayer("Tile");
            }
            // If it's a check indicator but not hover, restore check layer
            else if (IsTileInCheck(pos) && pos != currentHover)
            {
                if (kingInCheckPosition.HasValue && pos == kingInCheckPosition.Value)
                    tiles[pos.x, pos.y].layer = LayerMask.NameToLayer("CheckKing");
                else if (checkingPiecePosition.HasValue && pos == checkingPiecePosition.Value)
                    tiles[pos.x, pos.y].layer = LayerMask.NameToLayer("CheckPiece");
            }
        }
        availableMoves.Clear();
    }
    
    //Checkmate
    private void CheckMate(int team)
    {
        DisplayVictory(team);
    }
    
    private bool CheckForInsufficientMaterial()
    {
        List<ChessPiece> whitePieces = new List<ChessPiece>();
        List<ChessPiece> blackPieces = new List<ChessPiece>();

        for (int x = 0; x < TILE_COUNT_X; x++)
        for (int y = 0; y < TILE_COUNT_Y; y++)
            if (chessPieces[x, y] != null)
            {
                if (chessPieces[x, y].team == 0) whitePieces.Add(chessPieces[x, y]);
                else                             blackPieces.Add(chessPieces[x, y]);
            }

        // K vs K
        if (whitePieces.Count == 1 && blackPieces.Count == 1)
            return true;

        // K vs K + minor (N or B)
        if (whitePieces.Count == 1 && blackPieces.Count == 2)
            return blackPieces.Exists(p => p.type == ChessPieceType.Knight || p.type == ChessPieceType.Bishop);

        if (blackPieces.Count == 1 && whitePieces.Count == 2)
            return whitePieces.Exists(p => p.type == ChessPieceType.Knight || p.type == ChessPieceType.Bishop);

        // K+B vs K+B — same color complex
        if (whitePieces.Count == 2 && blackPieces.Count == 2)
        {
            var wBishop = whitePieces.Find(p => p.type == ChessPieceType.Bishop);
            var bBishop = blackPieces.Find(p => p.type == ChessPieceType.Bishop);

            if (wBishop != null && bBishop != null)
            {
                // same color = (x+y) even or odd match → cannot capture each other
                bool sameColor = (wBishop.currentX + wBishop.currentY) % 2 ==
                                 (bBishop.currentX + bBishop.currentY) % 2;
                if (sameColor) return true;
            }
        }

        return false;
    }

    private bool CheckForStalemate()
    {
        if (moveList.Count == 0) return false;

        var lastMove = moveList[moveList.Count - 1];
        int targetTeam = (chessPieces[lastMove[1].x, lastMove[1].y].team == 0) ? 1 : 0;

        List<ChessPiece> defendingPieces = new List<ChessPiece>();
        List<ChessPiece> attackingPieces  = new List<ChessPiece>();
        ChessPiece targetKing = null;

        for (int x = 0; x < TILE_COUNT_X; x++)
        for (int y = 0; y < TILE_COUNT_Y; y++)
            if (chessPieces[x, y] != null)
            {
                if (chessPieces[x, y].team == targetTeam)
                {
                    defendingPieces.Add(chessPieces[x, y]);
                    if (chessPieces[x, y].type == ChessPieceType.King)
                        targetKing = chessPieces[x, y];
                }
                else
                    attackingPieces.Add(chessPieces[x, y]);
            }

        if (targetKing == null) return false;

        // Is king currently attacked?
        List<Vector2Int> attackMoves = new List<Vector2Int>();
        foreach (var piece in attackingPieces)
        {
            var moves = piece.GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
            attackMoves.AddRange(moves);
        }

        if (ContainsValidMove(ref attackMoves, new Vector2Int(targetKing.currentX, targetKing.currentY)))
            return false; // in check → not stalemate

        // Does any defending piece (including king) have a legal move?
        foreach (var piece in defendingPieces)
        {
            var moves = piece.GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
            SimulateMoveForSinglePiece(piece, ref moves, targetKing);
            if (moves.Count > 0) return false;
        }

        return true;
    }

    private void DisplayVictory(int winningTeam)
    {
        victoryScreen.SetActive(true);
        victoryScreen.transform.GetChild(winningTeam).gameObject.SetActive(true);
    }

    public void OnResetButton()
    {
        // Victory UI
        if (victoryScreen != null)
        {
            victoryScreen.transform.GetChild(0).gameObject.SetActive(false);
            victoryScreen.transform.GetChild(1).gameObject.SetActive(false);
            victoryScreen.SetActive(false);
        }

        // Draw UI
        if (drawScreen != null)
        {
            for (int i = 0; i < drawScreen.transform.childCount; i++)
                drawScreen.transform.GetChild(i).gameObject.SetActive(false);
            drawScreen.SetActive(false);
        }

        currentPiece = null;
        availableMoves.Clear();
        moveList.Clear();
        fiftyMoveCounter = 0;
        ClearCheckIndicators();

        // Clean up pieces
        for (int x = 0; x < TILE_COUNT_X; x++)
        for (int y = 0; y < TILE_COUNT_Y; y++)
        {
            if (chessPieces[x, y] != null)
                Destroy(chessPieces[x, y].gameObject);
            chessPieces[x, y] = null;
        }

        foreach (var p in deadWhites) Destroy(p.gameObject);
        foreach (var p in deadBlacks) Destroy(p.gameObject);
        deadWhites.Clear();
        deadBlacks.Clear();

        GenerateAllPieces();
        PositionAllPieces();

        isWhiteTurn = true;
    }

    public void OnExitButton()
    {
        Application.Quit();
    }
    
    private void ProcessSpecialMove()
    {
        if (specialMove == SpecialMove.EnPassant)
        {
            var newMove = moveList[moveList.Count - 1];
            ChessPiece myPawn = chessPieces[newMove[1].x, newMove[1].y];
            var targetPawnPosition = moveList[moveList.Count - 2];
            ChessPiece enemyPawn = chessPieces[targetPawnPosition[1].x, targetPawnPosition[1].y];


            if (myPawn.currentX == enemyPawn.currentX)
            {
                if (myPawn.currentY == (enemyPawn.currentY - 1) || myPawn.currentY == (enemyPawn.currentY + 1))
                {
                    Debug.Log(myPawn.currentY == (enemyPawn.currentY - 1) || myPawn.currentY == (enemyPawn.currentY + 1));
                    if (enemyPawn.team == 0)
                    {
                        deadWhites.Add(enemyPawn);
                        enemyPawn.SetScale(Vector3.one * deathSize);
                        enemyPawn.SetPosition(
                            new Vector3(8*tileSize, yOffset+deathOffset, -1*tileSize)
                            - bounds + new Vector3(tileSize / 2, 0, tileSize / 2) 
                                     + (Vector3.forward * deathSpacing) * deadWhites.Count);
                        
                    }
                    else
                    {
                        deadBlacks.Add(enemyPawn);
                        enemyPawn.SetScale(Vector3.one * deathSize);
                        enemyPawn.SetPosition(
                            new Vector3(-1 *tileSize, yOffset+deathOffset, 8*tileSize)
                            - bounds + new Vector3(tileSize / 2, 0, tileSize / 2) 
                                     + (Vector3.back * deathSpacing) * deadBlacks.Count);
                    }
                    chessPieces[enemyPawn.currentX, enemyPawn.currentY] = null;
                    
                }
            }
        }
        
        if (specialMove == SpecialMove.Promotion)
        {
            Vector2Int[] lastMove = moveList[moveList.Count - 1];
            ChessPiece targetPawn = chessPieces[lastMove[1].x, lastMove[1].y];

            if (targetPawn.type == ChessPieceType.Pawn)
            {
                if (targetPawn.team == 0 && lastMove[1].y == 7)
                {
                    ChessPiece newQueen = SpawnSinglePiece(ChessPieceType.Queen, 0);
                    newQueen.transform.position = chessPieces[lastMove[1].x, lastMove[1].y].transform.position;
                    Destroy(chessPieces[lastMove[1].x, lastMove[1].y].gameObject);
                    chessPieces[lastMove[1].x, lastMove[1].y] = newQueen;
                    PositionSinglePiece(lastMove[1].x, lastMove[1].y);
                }
                if (targetPawn.team == 1 && lastMove[1].y == 0)
                {
                    ChessPiece newQueen = SpawnSinglePiece(ChessPieceType.Queen, 1);
                    newQueen.transform.position = chessPieces[lastMove[1].x, lastMove[1].y].transform.position;
                    Destroy(chessPieces[lastMove[1].x, lastMove[1].y].gameObject);
                    chessPieces[lastMove[1].x, lastMove[1].y] = newQueen;
                    PositionSinglePiece(lastMove[1].x, lastMove[1].y);
                }
            }
            
        }

        if (specialMove == SpecialMove.Castle)
        {
            Vector2Int[] lastMove = moveList[moveList.Count - 1];

            if (lastMove[1].x == 2)
            {
                if (lastMove[1].y == 0)
                {
                    ChessPiece rook = chessPieces[0, 0];
                    chessPieces[3, 0] = rook;
                    PositionSinglePiece(3, 0);
                    chessPieces[0, 0] = null;
                }
                else if(lastMove[1].y == 7)
                {
                    ChessPiece rook = chessPieces[7, 7];
                    chessPieces[3, 7] = rook;
                    PositionSinglePiece(3, 7);
                    chessPieces[0, 7] = null;
                }
            }
            else if (lastMove[1].x == 6)
            {
                if (lastMove[1].y == 0)
                {
                    ChessPiece rook = chessPieces[7, 0];
                    chessPieces[5, 0] = rook;
                    PositionSinglePiece(5, 0);
                    chessPieces[7, 0] = null;
                }
                else if(lastMove[1].y == 7)
                {
                    ChessPiece rook = chessPieces[7, 7];
                    chessPieces[5, 7] = rook;
                    PositionSinglePiece(5, 7);
                    chessPieces[7, 7] = null;
                }
            }
        }
    }
    
    private void PreventCheck()
    {
        ChessPiece targetKing = null;
        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (chessPieces[x, y] != null)
                    if (chessPieces[x,y].type == ChessPieceType.King)
                        if (chessPieces[x, y].team == currentPiece.team)
                            targetKing = chessPieces[x, y];
        
        SimulateMoveForSinglePiece(currentPiece,ref availableMoves, targetKing);
        
    }
    
    private void SimulateMoveForSinglePiece(ChessPiece currentPiece, ref List<Vector2Int> moves, ChessPiece targetKing)
    {
        int actualX = currentPiece.currentX;
        int actualY = currentPiece.currentY;
        
        List<Vector2Int> movesToRemove = new List<Vector2Int>();


        for (int i = 0; i < moves.Count; i++)
        {
            int simX = moves[i].x;
            int simY = moves[i].y;
            
            
            Vector2Int kingPosThisSim = new Vector2Int(targetKing.currentX, targetKing.currentY);

            if (currentPiece.type == ChessPieceType.King)
            {
                kingPosThisSim = new Vector2Int(simX, simY);
            }
            
            ChessPiece[,] simulation = new ChessPiece[TILE_COUNT_X, TILE_COUNT_Y];
            List<ChessPiece> simAttackPieces = new List<ChessPiece>();

            for (int x = 0; x < TILE_COUNT_X; x++)
            {
                for (int y = 0; y < TILE_COUNT_Y; y++)
                {
                    if (chessPieces[x, y] != null)
                    {
                        simulation[x, y] = chessPieces[x, y];
                        if (simulation[x,y].team != currentPiece.team)
                        {
                            simAttackPieces.Add(simulation[x, y]);
                        }
                    }
                }
            }
                
            simulation[actualX, actualY] = null;
            currentPiece.currentX = simX;
            currentPiece.currentY = simY;
            simulation[simX, simY] = currentPiece;
            
            var deadPiece = simAttackPieces.Find(c => c.currentX == simX && c.currentY == simY);
            if (deadPiece != null)
                simAttackPieces.Remove(deadPiece);

            List<Vector2Int> simMoves = new List<Vector2Int>();
            for (int x = 0; x < simAttackPieces.Count; x++)
            {
                var pieceMoves = simAttackPieces[x].GetAvailableMoves(ref simulation, TILE_COUNT_X, TILE_COUNT_Y);
                for(int b = 0; b < pieceMoves.Count; b++)
                    simMoves.Add(pieceMoves[b]);
                
            }

            if (ContainsValidMove(ref simMoves, kingPosThisSim))
            {
               movesToRemove.Add(moves[i]); 
            }

            currentPiece.currentX = actualX;
            currentPiece.currentY = actualY;

        }

        for (int i = 0; i < movesToRemove.Count; i++)
        {
            moves.Remove(movesToRemove[i]);
        }
    }

    private bool CheckForCheckmate()
    {
        var lastMove = moveList[moveList.Count - 1];
        int targetTeam = (chessPieces[lastMove[1].x, lastMove[1].y].team == 0) ? 1 : 0;
        List<ChessPiece> attackingPieces = new List<ChessPiece>();
        List<ChessPiece> defendingPieces = new List<ChessPiece>();
        ChessPiece targetKing = null;
        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (chessPieces[x, y] != null)
                {
                    if (chessPieces[x, y].team == targetTeam)
                    {
                        defendingPieces.Add(chessPieces[x, y]);
                        if (chessPieces[x, y].type == ChessPieceType.King)
                        {
                            targetKing = chessPieces[x, y];
                        }
                        
                    }
                    else
                    {
                        attackingPieces.Add(chessPieces[x, y]);
                    }
                }
        
        List<Vector2Int> currentAvailingMoves = new List<Vector2Int>();
        for (int i = 0; i < attackingPieces.Count; i++)
        {
            var pieceMoves = attackingPieces[i].GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
            for (int b = 0; b < pieceMoves.Count; b++)
                currentAvailingMoves.Add(pieceMoves[b]);
        }

        if (ContainsValidMove(ref currentAvailingMoves, new Vector2Int(targetKing.currentX, targetKing.currentY)))
        {
            for (int x = 0; x < defendingPieces.Count; x++)
            {
                List<Vector2Int> defendingMoves = defendingPieces[x].GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
                SimulateMoveForSinglePiece(defendingPieces[x],ref defendingMoves, targetKing);

                if (defendingMoves.Count != 0)
                    return false;
            }
            return true;
        }
        return false;
    }
    
    // Operations
    private bool ContainsValidMove(ref List<Vector2Int> moves, Vector2Int pos)
    {
        for(int i = 0; i < moves.Count; i++)
            if (moves[i].x == pos.x && moves[i].y == pos.y)
                return true;
        return false;
    }

    private void DrawGame(DrawType drawType)
    {
        if (drawScreen == null) return;

        drawScreen.SetActive(true);

        // Assumes drawScreen has 3 children (index 0 = Stalemate, 1 = Insufficient, 2 = 50-move)
        for (int i = 0; i < 3; i++)
            drawScreen.transform.GetChild(i).gameObject.SetActive(false);

        if (drawType != DrawType.None)
            drawScreen.transform.GetChild((int)drawType - 1).gameObject.SetActive(true);
    }

    // ──────────────────────────────────────────────────────────────
    //  MoveTo – main logic + draw checks
    // ──────────────────────────────────────────────────────────────

    private bool MoveTo(ChessPiece chessPiece, int hitPositionX, int hitPositionY)
    {
        if (!ContainsValidMove(ref availableMoves, new Vector2Int(hitPositionX, hitPositionY)))
            return false;
        
        Vector2Int previousPosition = new Vector2Int(currentPiece.currentX, currentPiece.currentY);
        
        RemoveHighlightTiles();
        ClearCheckIndicators();

        if (chessPieces[hitPositionX, hitPositionY] != null)
        {
            ChessPiece otherChessPiece = chessPieces[hitPositionX, hitPositionY];

            if (chessPiece.team == otherChessPiece.team)
                return false;

            if (otherChessPiece.team == 0)
            {
                if (otherChessPiece.type == ChessPieceType.King)
                    CheckMate(1);
                  
                deadWhites.Add(otherChessPiece);
                otherChessPiece.SetScale(Vector3.one * deathSize);
                otherChessPiece.SetPosition(
                    new Vector3(8*tileSize, yOffset+deathOffset, -1*tileSize)
                    - bounds + new Vector3(tileSize / 2, 0, tileSize / 2) 
                             + (Vector3.forward * deathSpacing) * deadWhites.Count);
            }
            else
            {
                if (otherChessPiece.type == ChessPieceType.King)
                    CheckMate(0);
                
                deadBlacks.Add(otherChessPiece);
                otherChessPiece.SetScale(Vector3.one * deathSize);
                otherChessPiece.SetPosition(
                    new Vector3(-1 *tileSize, yOffset+deathOffset, 8*tileSize)
                    - bounds + new Vector3(tileSize / 2, 0, tileSize / 2) 
                             + (Vector3.back * deathSpacing) * deadBlacks.Count);
                
            }
        }
        
        bool isCapture = chessPieces[hitPositionX, hitPositionY] != null;
        chessPieces[hitPositionX, hitPositionY] = chessPiece;
        chessPieces[previousPosition.x, previousPosition.y] = null;
        
        PositionSinglePiece(hitPositionX, hitPositionY);

        isWhiteTurn = !isWhiteTurn;
        
        bool isPawnMove = chessPiece.type == ChessPieceType.Pawn;


        if (isPawnMove || isCapture)
            fiftyMoveCounter = 0;
        else
            fiftyMoveCounter++;
        
        moveList.Add(new Vector2Int[] {previousPosition, new Vector2Int(hitPositionX, hitPositionY)});
        Debug.Log(fiftyMoveCounter);

        ProcessSpecialMove();
        UpdateCheckIndicators();
        
        if (CheckForCheckmate())
        {
            CheckMate(currentPiece.team);
        }
        else
        {
            if (CheckForStalemate())
                DrawGame(DrawType.Stalemate);
            else if (CheckForInsufficientMaterial())
                DrawGame(DrawType.InsufficientMaterial);
            else if (fiftyMoveCounter >= 100)
                DrawGame(DrawType.FiftyMoveRule);
        }
        
        StartCameraFlip();
        
        return true;
    }
    
    
    private void StartCameraFlip()
    {
        if (cameraTransform == null) return;

        cameraStartRot = cameraTransform.rotation;
        cameraTargetRot = Quaternion.Euler(0, 180f, 0) * cameraStartRot;
    
        cameraStartPos = cameraTransform.position;
        cameraTargetPos = new Vector3(cameraStartPos.x, cameraStartPos.y, -cameraStartPos.z);
    
        cameraRotTimer = 0f;
        isCameraRotating = true;
    }
    
    private Vector2Int LookUpTileIndex(GameObject hitInfo)
    {
        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (hitInfo == tiles[x, y])
                    return new Vector2Int(x, y);
        
        return -Vector2Int.one; //Invalid
    }
}