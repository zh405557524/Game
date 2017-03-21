package com.soul.game;

import android.content.Context;
import android.graphics.Color;
import android.util.AttributeSet;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.RelativeLayout;
import android.widget.TextView;

/**
 * * @author soul
 *
 * @项目名:MyApplication
 * @包名: com.soul.game
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/25 14:53
 */

public class Card extends FrameLayout {


    private int num = 0;
    private TextView label;
    private boolean isAnimal = false;

    public Card(Context context) {
        this(context, null);
    }

    public Card(Context context, AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public Card(Context context, AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);
        LayoutParams lp = new LayoutParams(-1, -1);
        RelativeLayout relativeLayout = new RelativeLayout(context);
        label = new TextView(context);
        label.setTextColor(Color.BLACK);
        label.setTextSize(32);
        label.setGravity(Gravity.CENTER);
        lp.setMargins(0, 0, 0, 0);
        relativeLayout.addView(label, lp);

        addView(relativeLayout, lp);
        setNum(0);
//        setBackground(ContextCompat.getDrawable(getContext(), R.drawable.f_empty));
    }

    public View getMoveView() {
        return label;
    }


    public int getNum() {
        return num;
    }

    public void setNum(int num) {
        this.num = num;
        //        animation at the time when to stop
        if (isAnimal) {
            return;
        }
        if (num <= 0) {
//            label.setBackground(ContextCompat.getDrawable(getContext(), R.drawable.f_empty));
            label.setVisibility(GONE);
        } else {
            label.setVisibility(VISIBLE);
            label.setBackground(PictureChooseUtils.getDrawable(num));
        }
    }

    public boolean equals(Card card) {
        return getNum() == card.getNum();
    }

    public View getView() {
        return this;
    }

    /**
     * animal start icon
     *
     */
    public void startAnimal() {
        //        label.setBackground(PictureChooseUtils.getDrawable(num));
        isAnimal = true;

    }

    /**
     * animal end icon
     */
    public void endAnimal() {
        //        label.setBackground(PictureChooseUtils.getDrawable(num));
        isAnimal = false;
        setNum(num);
    }

}
