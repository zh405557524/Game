package com.soul.game;

/**
 * * @author soul
 *
 * @项目名:MyApplication
 * @包名: com.soul.game
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/30 2:08
 */

public class CardNum {

    public CardX x0;
    public CardX x1;
    public CardX x2;
    public CardX x3;


    public CardNum() {
        x0 = new CardX();
        x1 = new CardX();
        x2 = new CardX();
        x3 = new CardX();
    }

    public void setNum(int x, int y, int num) {
        switch (x) {
            case 0:
                setNumY(x0, y, num);
                break;
            case 1:
                setNumY(x1, y, num);
                break;
            case 2:
                setNumY(x2, y, num);
                break;
            case 3:
                setNumY(x3, y, num);
                break;
        }
    }

    private void setNumY(CardX i, int y, int num) {
        switch (y) {
            case 0:
                i.y0 = num;
                break;
            case 1:
                i.y1 = num;
                break;
            case 2:
                i.y2 = num;
                break;
            case 3:
                i.y3 = num;
                break;
        }
    }

    public int getNum(int x, int y) {
        switch (x) {
            case 0:
                return getNumY(x0, y);
            case 1:
                return getNumY(x1, y);
            case 2:
                return getNumY(x2, y);
            case 3:
                return getNumY(x3, y);
        }
        return 0;
    }

    private int getNumY(CardX x0, int y) {
        switch (y) {
            case 0:
                return x0.y0;
            case 1:
                return x0.y1;
            case 2:
                return x0.y2;
            case 3:
                return x0.y3;
        }
        return 0;
    }

    public class CardX {
        public int y0;
        public int y1;
        public int y2;
        public int y3;
    }
}
