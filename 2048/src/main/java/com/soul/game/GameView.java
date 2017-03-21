package com.soul.game;

import android.animation.Animator;
import android.animation.AnimatorSet;
import android.animation.ObjectAnimator;
import android.content.Context;
import android.content.DialogInterface;
import android.content.res.Resources;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Point;
import android.graphics.Rect;
import android.graphics.drawable.Drawable;
import android.media.MediaPlayer;
import android.support.v4.content.ContextCompat;
import android.support.v7.app.AlertDialog;
import android.util.AttributeSet;
import android.view.MotionEvent;
import android.view.animation.LinearInterpolator;
import android.widget.GridLayout;

import com.soul.game.utils.AnimalUtils;
import com.soul.game.utils.LogUtils;
import com.soul.game.utils.MediaPlayerUtils;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * * @author soul
 *
 * @项目名:MyApplication
 * @包名: com.soul.game
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/25 14:21
 */

public class GameView extends GridLayout {

    private Resources mRes;
    private MediaPlayer mMediaPlayer;
    private HashMap<Point, Point> mAnimalViewMap;
    private Bitmap mBitmapBackground;
    private Paint mPaint;

    public GameView(Context context) {
        this(context, null);
    }

    public GameView(Context context, AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public GameView(Context context, AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);
        initGameView();
    }


    private void initGameView() {
        isAnimal = false;
        setColumnCount(4);
        Drawable drawable = ContextCompat.getDrawable(getContext(), R.drawable.main_background);
        setBackground(drawable);
        mMediaPlayer = MediaPlayerUtils.create(getContext(), R.raw.move);
        mBitmapBackground = BitmapFactory.decodeResource(getResources(), R.drawable.f_empty);
        mPaint = new Paint();
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        int childCount = getChildCount();
        if (mBitmapBackground != null) {
            for (int x = 0; x < childCount; x++) {
                // 指定图片绘制区域(左上角的四分之一)
                Rect src = new Rect(0, 0, mBitmapBackground.getWidth(), mBitmapBackground.getHeight());
                // 指定图片在屏幕上显示的区域
                Rect dst = new Rect(getChildAt(x).getLeft(), getChildAt(x).getTop(),
                        getChildAt(x).getLeft() + getChildAt(x).getWidth(),
                        getChildAt(x).getTop() + getChildAt(x).getHeight());
                canvas.drawBitmap(mBitmapBackground, src, dst, mPaint);
            }
        }
    }

    @Override
    protected void onSizeChanged(int w, int h, int oldw, int oldh) {
        super.onSizeChanged(w, h, oldw, oldh);
        LogUtils.i("onSizeChanged:");
        final int cardWidth = (Math.min(w, h) - 10) / 4;
        getHandler().post(new Runnable() {
            @Override
            public void run() {
                addCards(cardWidth, cardWidth);
                if (mCardNum == null) {
                    startGame();
                }
            }
        });
    }

    private void addCards(int cardWidth, int cardHeight) {
        if (mCardNum == null) {
            Card card;
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < 4; x++) {
                    card = new Card(getContext());
                    card.setNum(0);
                    addView(card, cardWidth, cardHeight);
                    cardsMap[x][y] = card;
                }
            }
        } else {
            Card card;
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < 4; x++) {
                    card = new Card(getContext());
                    card.setNum(mCardNum.getNum(x, y));
                    addView(card, cardWidth, cardHeight);
                    cardsMap[x][y] = card;
                }
            }
        }

    }


    public void startGame() {
        isAnimal = false;
        GameActivity.getMainActivity().clearScore();
        for (int y = 0; y < 4; y++) {
            for (int x = 0; x < 4; x++) {
                cardsMap[x][y].setNum(0);
            }
        }
        addRandomNum();
        addRandomNum();
    }


    private float startX, StartY, offsetX, offsetY;

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        if (isAnimal) {
            return super.onTouchEvent(event);
        }
        switch (event.getAction()) {
            case MotionEvent.ACTION_DOWN:
                startX = event.getX();
                StartY = event.getY();
                break;
            case MotionEvent.ACTION_HOVER_MOVE:
                offsetX = event.getX() - startX;
                offsetY = event.getY() - StartY;
                break;
            case MotionEvent.ACTION_UP:
                offsetX = event.getX() - startX;
                offsetY = event.getY() - StartY;

                if (Math.abs(offsetX) > Math.abs(offsetY)) {

                    if (offsetX < -5) {

                        swipeLeft();
                        LogUtils.i("left");

                    } else if (offsetX > 5) {
                        swipeRight();

                        LogUtils.i("right");
                    }
                } else {
                    if (offsetY < -5) {
                        swipeUp();
                        LogUtils.i("up");
                    } else if (offsetY > 5) {
                        swipeDown();
                        LogUtils.i("down");
                    }
                }
                break;
        }
        return true;
    }

    private void addRandomNum() {
        emptyPoints.clear();
        for (int y = 0; y < 4; y++) {
            for (int x = 0; x < 4; x++) {
                if (cardsMap[x][y].getNum() <= 0) {
                    emptyPoints.add(new Point(x, y));
                }
            }
        }
        Point point = emptyPoints.remove((int) (Math.random() * emptyPoints.size()));
        cardsMap[point.x][point.y].setNum(Math.random() > 0.1 ? 2 : 4);
    }

    private boolean isAnimal = false;

    private void swipeLeft() {
        boolean merge = false;
        if (mAnimalViewMap != null)
            mAnimalViewMap.clear();

        mAnimalViewMap = null;
        mAnimalViewMap = new HashMap<>();

        for (int y = 0; y < 4; y++) {
            for (int x = 0; x < 4; x++) {
                for (int x1 = x + 1; x1 < 4; x1++) {
                    if (cardsMap[x1][y].getNum() > 0) {

                        if (cardsMap[x][y].getNum() <= 0) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x1][y].startAnimal();


                            cardsMap[x][y].setNum(cardsMap[x1][y].getNum());
                            cardsMap[x1][y].setNum(0);

                            addPoint(y, y, x, x1);
                            merge = true;
                            x--;
                        } else if (cardsMap[x][y].equals(cardsMap[x1][y])) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x1][y].startAnimal();

                            cardsMap[x][y].setNum(cardsMap[x][y].getNum() * 2);
                            cardsMap[x1][y].setNum(0);
                            GameActivity.getMainActivity().addScore(cardsMap[x][y].getNum());
                            merge = true;
                            addPoint(y, y, x, x1);
                        }
                        break;
                    }
                }
            }
        }

        startMoveAnimal(merge, true, true).start();
    }

    private void addPoint(int y, int y1, int x, int x1) {
        Point point1;
        Point point2;
        point1 = new Point();
        point2 = new Point();

        point1.x = x;
        point2.x = x1;

        point1.y = y;
        point2.y = y1;
        mAnimalViewMap.put(point2, point1);
    }

    private AnimatorSet startMoveAnimal(boolean merge, boolean isPositive, final boolean isX) {
        isAnimal = true;
        AnimatorSet animatorSet = AnimalUtils.createAnimatorSet();
        for (Map.Entry<Point, Point> entry : mAnimalViewMap.entrySet()) {
            Point cardForm = entry.getKey();
            Point cardTo = entry.getValue();
            // Create the individual animators
            ObjectAnimator pageDrift = ObjectAnimator.ofFloat(cardsMap[cardForm.x][cardForm.y], isX ? "translationX" : "translationY",
                    0, isX ? cardsMap[cardForm.x][cardForm.y].getWidth() * (cardTo.x - cardForm.x) :
                            cardsMap[cardForm.x][cardForm.y].getHeight() * (cardTo.y - cardForm.y)
            );
            pageDrift.setDuration(PictureChooseUtils.getsAnimalDuration());
            //            pageDrift.setInterpolator(new LogDecelerateInterpolator(100, 0));
            pageDrift.setInterpolator(new LinearInterpolator());
            //            pageDrift.setStartDelay(600);
            animatorSet.play(pageDrift);
        }

        final boolean finalMerge = merge;
        animatorSet.addListener(new Animator.AnimatorListener() {
            @Override
            public void onAnimationStart(Animator animator) {

            }

            @Override
            public void onAnimationEnd(Animator animator) {
                isAnimal = false;
                LogUtils.i("onAnimationEnd");
                for (Map.Entry<Point, Point> entry : mAnimalViewMap.entrySet()) {
                    Point cardForm = entry.getKey();
                    Point cardTo = entry.getValue();
                    // Create the individual animators
                    ObjectAnimator pageDrift = ObjectAnimator.ofFloat(cardsMap[cardForm.x][cardForm.y], isX ? "translationX" : "translationY",
                            0, 0);
                    pageDrift.setDuration(0);
                    pageDrift.start();
                }
                for (int y = 0; y < 4; y++) {
                    for (int x = 0; x < 4; x++) {
                        cardsMap[x][y].endAnimal();
                    }
                }
                if (finalMerge) {
                    addRandomNum();
                    checkComplete();
                    playMusic(getContext());
                    LogUtils.i("playMusic");
                }
            }

            @Override
            public void onAnimationCancel(Animator animator) {

            }

            @Override
            public void onAnimationRepeat(Animator animator) {

            }
        });
        return animatorSet;
    }

    private void swipeRight() {

        if (mAnimalViewMap != null)
            mAnimalViewMap.clear();
        mAnimalViewMap = null;
        mAnimalViewMap = new HashMap<>();

        boolean merge = false;
        for (int y = 0; y < 4; y++) {
            for (int x = 3; x >= 0; x--) {
                for (int x1 = x - 1; x1 >= 0; x1--) {
                    if (cardsMap[x1][y].getNum() > 0) {
                        if (cardsMap[x][y].getNum() <= 0) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x1][y].startAnimal();
                            cardsMap[x][y].setNum(cardsMap[x1][y].getNum());
                            cardsMap[x1][y].setNum(0);
                            addPoint(y, y, x, x1);
                            x++;
                            merge = true;
                        } else if (cardsMap[x][y].equals(cardsMap[x1][y])) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x1][y].startAnimal();
                            cardsMap[x][y].setNum(cardsMap[x][y].getNum() * 2);
                            cardsMap[x1][y].setNum(0);
                            GameActivity.getMainActivity().addScore(cardsMap[x][y].getNum());
                            merge = true;
                            addPoint(y, y, x, x1);
                        }
                        break;
                    }
                }
            }
        }

        startMoveAnimal(merge, false, true).start();
    }

    private void swipeUp() {
        boolean merge = false;
        if (mAnimalViewMap != null)
            mAnimalViewMap.clear();
        mAnimalViewMap = null;
        mAnimalViewMap = new HashMap<>();
        for (int x = 0; x < 4; x++) {
            for (int y = 0; y < 4; y++) {
                for (int y1 = y + 1; y1 < 4; y1++) {
                    if (cardsMap[x][y1].getNum() > 0) {
                        if (cardsMap[x][y].getNum() <= 0) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x][y1].startAnimal();
                            cardsMap[x][y].setNum(cardsMap[x][y1].getNum());
                            cardsMap[x][y1].setNum(0);
                            addPoint(y, y1, x, x);
                            y--;
                            merge = true;
                        } else if (cardsMap[x][y].equals(cardsMap[x][y1])) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x][y1].startAnimal();
                            cardsMap[x][y].setNum(cardsMap[x][y].getNum() * 2);
                            cardsMap[x][y1].setNum(0);
                            GameActivity.getMainActivity().addScore(cardsMap[x][y].getNum());
                            merge = true;
                            addPoint(y, y1, x, x);
                        }
                        break;
                    }
                }
            }
        }
        startMoveAnimal(merge, false, false).start();

    }

    private void swipeDown() {
        if (mAnimalViewMap != null)
            mAnimalViewMap.clear();
        mAnimalViewMap = null;
        mAnimalViewMap = new HashMap<>();
        boolean merge = false;
        for (int x = 0; x < 4; x++) {
            for (int y = 3; y >= 0; y--) {
                for (int y1 = y - 1; y1 >= 0; y1--) {
                    if (cardsMap[x][y1].getNum() > 0) {
                        if (cardsMap[x][y].getNum() <= 0) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x][y1].startAnimal();
                            cardsMap[x][y].setNum(cardsMap[x][y1].getNum());
                            cardsMap[x][y1].setNum(0);
                            addPoint(y, y1, x, x);
                            y++;
                            merge = true;
                        } else if (cardsMap[x][y].equals(cardsMap[x][y1])) {
                            cardsMap[x][y].startAnimal();
                            cardsMap[x][y1].startAnimal();
                            cardsMap[x][y].setNum(cardsMap[x][y].getNum() * 2);
                            cardsMap[x][y1].setNum(0);
                            GameActivity.getMainActivity().addScore(cardsMap[x][y].getNum());
                            merge = true;
                            addPoint(y, y1, x, x);
                        }
                        break;
                    }
                }
            }
        }
        startMoveAnimal(merge, false, false).start();
    }


    /**
     * 有空位，或者周围有相同的数 游戏便没有结束
     */
    private void checkComplete() {
        boolean complete = true;
        All:
        for (int y = 0; y < 4; y++) {
            for (int x = 0; x < 4; x++) {
                if (cardsMap[x][y].getNum() == 0 || (x > 0 && cardsMap[x][y].equals(cardsMap[x - 1][y]))
                        || (x < 3 && cardsMap[x][y].equals(cardsMap[x + 1][y]))
                        || (y > 0 && cardsMap[x][y].equals(cardsMap[x][y - 1]))
                        || (y < 3 && cardsMap[x][y].equals(cardsMap[x][y + 1]))) {
                    complete = false;
                    break All;
                }
            }
        }
        if (complete) {
            new AlertDialog.Builder(getContext())
                    .setTitle("您好")
                    .setMessage("游戏结束")
                    .setPositiveButton("重来", new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialogInterface, int i) {
                            startGame();
                        }
                    }).show();
        }
    }


    private Card[][] cardsMap = new Card[4][4];

    private List<Point> emptyPoints = new ArrayList<Point>();

    public void playMusic(Context context) {
        try {
            mMediaPlayer.start();
        } catch (Exception e) {
            e.printStackTrace();
        } finally {

        }
    }

    public Card[][] getCardsMap() {
        return cardsMap;
    }

    public void setCardNum(CardNum cardsMap) {
        LogUtils.i("CardNum:" + cardsMap);
        mCardNum = cardsMap;
    }

    private CardNum mCardNum;
}























