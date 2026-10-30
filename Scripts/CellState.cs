public enum CellState
{
    Empty = 0,    // 空白
    Marked = 1,   // 普通 X，可取消
    Placed = 2,   // 正确动物，锁定
    Rejected = 3  // 红 X，锁定
}