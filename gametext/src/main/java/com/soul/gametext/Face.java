package com.soul.gametext;

import android.content.Context;
import android.graphics.BitmapFactory;
import android.graphics.Point;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.gametext
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/31 14:54
 */

public class Face extends Sprite {

    Context mContext;

    private Point clickPoint;//用户点击的位置-->欧诺个只小兰移动的方向
    int vx;//水平方向的速度
    int vy;//垂直方向的速度
    int vz = 5;//斜边的速度

    public Face(Point point,Point clickPoint, Context context) {
        super(null, point);
        this.clickPoint = clickPoint;
        mContext = context;
        mBitmap = BitmapFactory.decodeResource(context.getResources(), R.drawable.rating_small);
        int X = clickPoint.x- this.mPoint.x;
        int Y = clickPoint.y -this.mPoint.y;
        int Z = (int) Math.sqrt(X*X+Y*Y);
        vy  =Y*vz/Z;
        vx = X*vz/Z;

    }

    /**
     * 控制脸的移动
     */
    public void move() {
        //        this.mPoint   =this.mPoint.x+x方向的距离//s  = vx;
        //        this.mPoint.y = this.mPoint.y+y方向的距离//s = vy;
                    this.mPoint.x   =this.mPoint.x+vx;//s  = vx;
                    this.mPoint.y = this.mPoint.y+vy;//s = vy;


    }

}
